using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.AuditLogs.Queries.GetAuditLogs;

namespace ROCloud.Application.Features.AuditLogs.Queries.ExportAuditLogs;

/// <summary>One CSV row of the activity log export (flat, header order = property order).</summary>
public sealed record AuditLogExportRow(
    DateTime Timestamp,
    string? User,
    string Module,
    string Action,
    string? Entity,
    int? Status,
    string? IpAddress);

/// <summary>Exports the filtered activity log to CSV (capped). Owner only (via the controller).</summary>
public sealed record ExportAuditLogsQuery(AuditLogFilterDto Filter) : IRequest<byte[]>;

public class ExportAuditLogsQueryHandler : IRequestHandler<ExportAuditLogsQuery, byte[]>
{
    private const int MaxRows = 10_000;

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IReportExporter _exporter;

    public ExportAuditLogsQueryHandler(IAppDbContext db, ITenantContext tenant, IReportExporter exporter)
    {
        _db = db;
        _tenant = tenant;
        _exporter = exporter;
    }

    public async Task<byte[]> Handle(ExportAuditLogsQuery request, CancellationToken ct)
    {
        var query = GetAuditLogsQueryHandler.ApplyFilter(_db.AuditLogs, _tenant.TenantId, request.Filter);

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(MaxRows)
            .Select(a => new AuditLogExportRow(
                a.CreatedAt,
                _db.Users.Where(u => u.Id == a.UserId).Select(u => u.Name).FirstOrDefault(),
                a.Module, a.Action, a.EntityName, a.StatusCode, a.IpAddress))
            .ToListAsync(ct);

        return _exporter.ToCsv(rows);
    }
}
