using System.Security.Authentication;
using System.Text;
using AspNetCoreRateLimit;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Npgsql;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ROCloud.API.HealthChecks;
using ROCloud.API.Localization;
using ROCloud.API.Logging;
using ROCloud.API.Middleware;
using ROCloud.Infrastructure.BackgroundJobs;
using ROCloud.Application;
using ROCloud.Application.Common.Security;
using ROCloud.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

// Pin the app's display/business timezone (App:TimeZone, default IST) once at the composition root,
// so delivery-day logic and recurring-job wall-clock times are independent of the host machine and
// stay in lock-step with the DB session timezone and the portals' display offset.
ROCloud.Application.Common.AppTimeZone.Configure(builder.Configuration["App:TimeZone"]);

// ─── Serilog ───────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) =>
{
    lc.MinimumLevel.Information()
      // Quiet the noisy framework sources so the logs table keeps meaningful entries, not
      // per-request chatter (route matching, "writing value as Json", every EF SQL statement…).
      // Our own request summary (UseSerilogRequestLogging) is a different source and is unaffected.
      .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
      .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
      .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
      .MinimumLevel.Override("System", LogEventLevel.Warning)
      .Enrich.FromLogContext()
      .Enrich.With(new SensitiveDataEnricher())
      .WriteTo.Console();

    // Production: also write to the PostgreSQL "logs" table.
    if (!ctx.HostingEnvironment.IsDevelopment())
    {
        var conn = ctx.Configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(conn))
        {
            var columnWriters = new Dictionary<string, ColumnWriterBase>
            {
                ["message"] = new RenderedMessageColumnWriter(),
                ["message_template"] = new MessageTemplateColumnWriter(),
                ["level"] = new LevelColumnWriter(),
                ["timestamp"] = new TimestampColumnWriter(),
                ["exception"] = new ExceptionColumnWriter(),
                ["log_event"] = new LogEventSerializedColumnWriter(),
                ["properties"] = new PropertiesColumnWriter()
            };
            lc.WriteTo.PostgreSQL(conn, "logs", columnWriters, needAutoCreateTable: true);
        }
    }
});

// ─── Kestrel hardening (guide §10.9 / §10.16) ──────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;                       // no "Server: Kestrel"
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;  // 50 MB
    options.ConfigureHttpsDefaults(https =>
        https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13);
});

// ─── Application / Infrastructure services ─────────────────────────────────
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// ─── Authentication (JWT bearer) ───────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names as-issued ("sub", "jti", "tenant_id", "permissions", ...).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]
                    ?? throw new InvalidOperationException("Jwt:Secret is not configured")))
        };
        // Reject revoked tokens (logout / password change) — guide §10.3.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var jti = ctx.Principal?.FindFirst("jti")?.Value;
                if (jti is not null)
                {
                    var blocklist = ctx.HttpContext.RequestServices.GetRequiredService<TokenBlocklistService>();
                    if (await blocklist.IsBlockedAsync(jti))
                        ctx.Fail("Token revoked");
                }
            }
        };
    });
builder.Services.AddAuthorization();

// ─── CORS ──────────────────────────────────────────────────────────────────
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(opts => opts.AddPolicy("ROCloudPolicy", p => p
    .WithOrigins(corsOrigins)
    .SetIsOriginAllowedToAllowWildcardSubdomains()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ─── MVC + Swagger ─────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ─── Localization — Indian languages (guide §4c.4), from Localization:* config ─────────────
builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");
var defaultCulture = builder.Configuration["Localization:DefaultCulture"] ?? "en";
var supportedCultures = builder.Configuration.GetSection("Localization:SupportedCultures").Get<string[]>()
    ?? new[] { "en", "hi", "gu", "mr", "ta", "te", "kn", "pa", "bn" };
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    opts.SetDefaultCulture(defaultCulture)
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures);
    opts.RequestCultureProviders.Insert(0, new CustomHeaderRequestCultureProvider());
});

// ─── Rate limiting (guide §10.8) ───────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ─── Hangfire — recurring background jobs (guide §14) ──────────────────────
// The "hangfire" schema must exist and be writable by the app role. Like all DDL in this
// project, it is created by a privileged role (scripts/hangfire-setup.sql, run as postgres).
// We probe for it first so the API still boots if the schema hasn't been set up yet.
var hangfireConn = builder.Configuration.GetConnectionString("Default");
var hangfireEnabled = !string.IsNullOrWhiteSpace(hangfireConn) && HangfireSchemaReady(hangfireConn!);
if (hangfireEnabled)
{
    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(hangfireConn)));
    builder.Services.AddHangfireServer();
}

static bool HangfireSchemaReady(string connStr)
{
    try
    {
        using var conn = new NpgsqlConnection(connStr);
        conn.Open();
        using var check = new NpgsqlCommand(
            "SELECT 1 FROM information_schema.schemata WHERE schema_name = 'hangfire'", conn);
        if (check.ExecuteScalar() is not null) return true;

        // Not present — try to create it (works only if the app role has the privilege).
        using var create = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS hangfire", conn);
        create.ExecuteNonQuery();
        return true;
    }
    catch
    {
        return false;   // unreachable DB or insufficient privilege → run scripts/hangfire-setup.sql
    }
}

// ─── Health checks (guide §16) ─────────────────────────────────────────────
// /health/live = liveness (no dependencies); /health/ready & /startup = DB (+ Hangfire).
// v1 has no Redis (in-memory cache), so it is not part of readiness.
var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
if (hangfireEnabled)
    healthChecks.AddCheck("hangfire", () => Hangfire.JobStorage.Current is not null
        ? HealthCheckResult.Healthy("Hangfire scheduler up")
        : HealthCheckResult.Unhealthy("Hangfire storage not initialised"), tags: ["ready"]);

var app = builder.Build();
if (!hangfireEnabled)
    app.Logger.LogWarning(
        "Hangfire is disabled: the 'hangfire' schema is unavailable. Run scripts/hangfire-setup.sql as a privileged role to enable background jobs.");

// ─── Middleware pipeline ───────────────────────────────────────────────────
// Audit OUTSIDE the exception handler so it reads the FINAL response status the handler sets
// (e.g. a failed login that ExceptionMiddleware maps to 401). It is hardened to never throw.
app.UseMiddleware<AuditMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();                 // catch everything below
app.UseMiddleware<CorrelationIdMiddleware>();             // assign X-Request-Id, push to logs
app.UseSecurityHeaders(SecurityHeadersExtensions.BuildRoCloudPolicies(
    app.Configuration["App:ApiUrl"] ?? "https://api.rocloud.app",
    int.TryParse(app.Configuration["SecurityHeaders:HstsMaxAgeSeconds"], out var hsts) ? hsts : 31_536_000));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();

    // Strip identifying headers in production (guide §10.16).
    app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers.Remove("Server");
            ctx.Response.Headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });
        await next();
    });
}

app.UseSerilogRequestLogging();

var locOptions = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>();
app.UseRequestLocalization(locOptions.Value);

app.UseIpRateLimiting();
app.UseCors("ROCloudPolicy");
app.UseMiddleware<AntiCsrfMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();                    // after auth — needs tenant_id claim
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ─── Hangfire recurring jobs (guide §14) ───────────────────────────────────
// The raw /hangfire dashboard is intentionally NOT mounted. Background jobs are managed from the
// super-admin portal's Background Jobs page (api/platform/background-jobs — PlatformBackgroundJobsController),
// which reuses the normal bearer-token + platform-role auth instead of the dashboard's IP allowlist.
if (hangfireEnabled)
{
    // The static RecurringJob/JobStorage APIs (used below and by HangfireJobService's monitoring
    // reads) require JobStorage.Current. UseHangfireDashboard used to set it as a side effect; set
    // it explicitly from DI now that the dashboard is gone, before any static Hangfire call.
    JobStorage.Current = app.Services.GetRequiredService<JobStorage>();
    RecurringJobRegistration.Register(
        app.Services.GetRequiredService<ROCloud.Infrastructure.BackgroundJobs.RecurringJobSettingsStore>());
}

app.MapControllers();

// ─── Health probes (guide §16) ─────────────────────────────────────────────
// Anonymous for k8s/uptime probes; restrict at the ingress in production.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous().ExcludeFromDescription();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .AllowAnonymous().ExcludeFromDescription();
app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .AllowAnonymous().ExcludeFromDescription();

// Legacy/simple health endpoint for uptime monitors — anonymous, hidden from Swagger.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
}))
.AllowAnonymous()
.ExcludeFromDescription();

app.Run();
