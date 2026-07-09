using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ROCloud.Application.Common.Behaviours;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Auth.Services;
using ROCloud.Application.Services;

namespace ROCloud.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // Pipeline order: outer → inner (Logging → Authorization → Validation → Sanitization → handler).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(SanitizationBehaviour<,>));

        // HTML sanitisation (guide §10.5) — singletons. The strict one backs SanitizationBehaviour
        // (owner/customer rich text); the wider email one is used by the admin default-template editor.
        services.AddSingleton<IHtmlSanitizer, HtmlSanitizerService>();
        services.AddSingleton<IEmailHtmlSanitizer, EmailHtmlSanitizerService>();

        services.AddScoped<TenantProvisioner>();
        services.AddScoped<AuthTokenIssuer>();
        services.AddScoped<Features.Platform.Auth.Services.PlatformTokenIssuer>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<LoginAttemptService>();
        services.AddScoped<TokenBlocklistService>();

        return services;
    }
}
