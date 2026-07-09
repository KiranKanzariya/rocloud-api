using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.AuditLogs.Queries.GetAuditLogs;

/// <summary>A row in the audit log viewer.</summary>
public sealed record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string? UserName,
    string Module,
    string Action,
    string? EntityName,
    Guid? EntityId,
    string? IpAddress,
    int? StatusCode,
    DateTime CreatedAt,
    // Detail fields for the drill-down: the request payload (sensitive values already redacted) and the
    // client user-agent.
    string? NewValues,
    string? UserAgent);

public sealed record AuditLogFilterDto
{
    public Guid? UserId { get; init; }
    public string? Module { get; init; }
    public string? Action { get; init; }
    /// <summary>"success" (status &lt; 400 or none) or "failed" (status &gt;= 400).</summary>
    public string? Result { get; init; }
    /// <summary>Free-text match over module / entity name.</summary>
    public string? Search { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record GetAuditLogsQuery(AuditLogFilterDto Filter) : IRequest<PagedResult<AuditLogDto>>;

public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetAuditLogsQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<PagedResult<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 200);

        var query = ApplyFilter(_db.AuditLogs, _tenant.TenantId, f);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto(
                a.Id,
                a.UserId,
                // Resolve the actor's name for display; null for system/unauthenticated actions.
                _db.Users.Where(u => u.Id == a.UserId).Select(u => u.Name).FirstOrDefault(),
                a.Module, a.Action, a.EntityName, a.EntityId, a.IpAddress, a.StatusCode, a.CreatedAt,
                a.NewValues, a.UserAgent))
            .ToListAsync(ct);

        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }

    /// <summary>Shared filter for the viewer and the CSV export. AuditLog is not an ITenantEntity
    /// (tenant_id is nullable) so the tenant scope is applied explicitly here.</summary>
    public static IQueryable<AuditLog> ApplyFilter(IQueryable<AuditLog> source, Guid tenantId, AuditLogFilterDto f)
    {
        var query = source.Where(a => a.TenantId == tenantId);

        if (f.UserId is { } userId) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(f.Module)) query = query.Where(a => a.Module == f.Module);
        if (!string.IsNullOrWhiteSpace(f.Action)) query = query.Where(a => a.Action == f.Action);
        if (string.Equals(f.Result, "failed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => a.StatusCode >= 400);
        else if (string.Equals(f.Result, "success", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => a.StatusCode == null || a.StatusCode < 400);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = f.Search.Trim().ToLower();
            query = query.Where(a => a.Module.ToLower().Contains(term)
                                     || (a.EntityName != null && a.EntityName.ToLower().Contains(term)));
        }
        if (f.FromDate is { } from)
            query = query.Where(a => a.CreatedAt >= from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (f.ToDate is { } to)
            query = query.Where(a => a.CreatedAt <= to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

        return query;
    }
}
