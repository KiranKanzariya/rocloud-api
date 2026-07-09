using Microsoft.Extensions.Configuration;
using Npgsql;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Reports.Dtos;

namespace ROCloud.Infrastructure.Reports;

/// <summary>
/// Raw ADO.NET reporting (guide §9b, §12). Every query is parameterised and filtered by
/// <c>tenant_id</c>; the connection also sets <c>app.current_tenant_id</c> so the RLS policies
/// (payments/invoices/orders/deliveries/customers) let the rows through — defence in depth.
/// </summary>
public class ReportRepository : IReportRepository
{
    private readonly string _connStr;
    private readonly string _timeZone;

    public ReportRepository(IConfiguration config, IAppSettings settings)
    {
        _connStr = config.GetConnectionString("Default")
                   ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
        _timeZone = settings.TimeZone;
    }

    /// <summary>
    /// Opens a connection, pins the tenant for RLS, and pins the session timezone to the configured
    /// zone (App:TimeZone, default IST) so the date bucketing below (<c>::date</c>, EXTRACT and the
    /// delivered-since-midnight interval on timestamptz columns) is computed in that zone —
    /// independent of the host machine's timezone and matching what the portals display. Stored
    /// values remain UTC; only in-SQL derivation shifts.
    /// </summary>
    private async Task<NpgsqlConnection> OpenAsync(Guid tenantId, CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, false), set_config('TimeZone', @tz, false)", conn))
        {
            cmd.Parameters.AddWithValue("@tid", tenantId.ToString());
            cmd.Parameters.AddWithValue("@tz", _timeZone);
            await cmd.ExecuteScalarAsync(ct);
        }
        return conn;
    }

    public async Task<DailyCollectionDto[]> GetDailyCollectionAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        const string sql = @"
            WITH per_method AS (
                SELECT p.paid_at::date AS d, p.payment_method AS method, SUM(p.amount) AS amt
                FROM payments p
                WHERE p.tenant_id = @tenantId AND p.status = 'Completed'
                  AND p.paid_at::date BETWEEN @from AND @to
                GROUP BY p.paid_at::date, p.payment_method
            ),
            daily AS (
                SELECT p.paid_at::date AS d,
                       SUM(p.amount) AS total,
                       SUM(CASE WHEN p.payment_method = 'Cash' THEN p.amount ELSE 0 END) AS cash,
                       SUM(CASE WHEN p.payment_method <> 'Cash' THEN p.amount ELSE 0 END) AS digital,
                       COUNT(*) AS cnt
                FROM payments p
                WHERE p.tenant_id = @tenantId AND p.status = 'Completed'
                  AND p.paid_at::date BETWEEN @from AND @to
                GROUP BY p.paid_at::date
            ),
            top_methods AS (
                SELECT d, string_agg(method, ', ' ORDER BY amt DESC) AS methods
                FROM (SELECT d, method, amt,
                             ROW_NUMBER() OVER (PARTITION BY d ORDER BY amt DESC) AS rn
                      FROM per_method) r
                WHERE rn <= 3
                GROUP BY d
            )
            SELECT daily.d, daily.total, daily.cash, daily.digital, daily.cnt, top_methods.methods
            FROM daily
            LEFT JOIN top_methods ON top_methods.d = daily.d
            ORDER BY daily.d DESC";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@to", to);

        var rows = new List<DailyCollectionDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new DailyCollectionDto(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                (int)reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return rows.ToArray();
    }

    public async Task<MonthlyCollectionDto[]> GetMonthlyCollectionAsync(Guid tenantId, int year, CancellationToken ct)
    {
        const string sql = @"
            SELECT g.mon,
                   COALESCE(SUM(p.amount), 0) AS total,
                   COUNT(p.id) AS cnt
            FROM generate_series(1, 12) AS g(mon)
            LEFT JOIN payments p
              ON p.tenant_id = @tenantId AND p.status = 'Completed'
             AND EXTRACT(YEAR FROM p.paid_at) = @year
             AND EXTRACT(MONTH FROM p.paid_at) = g.mon
            GROUP BY g.mon
            ORDER BY g.mon";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@year", year);

        var rows = new List<MonthlyCollectionDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new MonthlyCollectionDto(
                year, reader.GetInt32(0), reader.GetDecimal(1), (int)reader.GetInt64(2)));
        }
        return rows.ToArray();
    }

    public async Task<DeliveryEfficiencyDto[]> GetDeliveryEfficiencyAsync(
        Guid tenantId, DateOnly date, CancellationToken ct)
    {
        // Avg delivery time = minutes from the scheduled day's midnight to delivered_at (proxy:
        // there is no dispatch timestamp in v1).
        const string sql = @"
            SELECT
                d.delivery_boy_id,
                COALESCE(u.name, 'Unassigned') AS boy_name,
                COUNT(*) AS assigned,
                COUNT(*) FILTER (WHERE d.status = 'Delivered') AS delivered,
                COUNT(*) FILTER (WHERE d.status = 'Pending') AS pending,
                COUNT(*) FILTER (WHERE d.status = 'Failed') AS failed,
                COALESCE(SUM(d.collected_amount) FILTER (WHERE d.status = 'Delivered'), 0) AS collected,
                AVG(EXTRACT(EPOCH FROM (d.delivered_at - d.scheduled_date::timestamp)) / 60.0)
                    FILTER (WHERE d.status = 'Delivered' AND d.delivered_at IS NOT NULL) AS avg_minutes
            FROM deliveries d
            LEFT JOIN users u ON u.id = d.delivery_boy_id
            WHERE d.tenant_id = @tenantId AND d.scheduled_date = @date
            GROUP BY d.delivery_boy_id, u.name
            ORDER BY delivered DESC";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@date", date);

        var rows = new List<DeliveryEfficiencyDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var assigned = (int)reader.GetInt64(2);
            var delivered = (int)reader.GetInt64(3);
            rows.Add(new DeliveryEfficiencyDto(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetString(1),
                assigned,
                delivered,
                (int)reader.GetInt64(4),
                (int)reader.GetInt64(5),
                reader.GetDecimal(6),
                assigned == 0 ? 0 : Math.Round(delivered * 100.0 / assigned, 1),
                reader.IsDBNull(7) ? null : Math.Round(reader.GetDouble(7), 1)));
        }
        return rows.ToArray();
    }

    public async Task<OutstandingDuesReportDto[]> GetOutstandingDuesAsync(
        Guid tenantId, DateOnly asOfDate, CancellationToken ct)
    {
        const string sql = @"
            SELECT i.customer_id, c.name, c.mobile,
                   SUM(i.bal) AS total,
                   SUM(CASE WHEN i.age <= 7 THEN i.bal ELSE 0 END) AS b0_7,
                   SUM(CASE WHEN i.age > 7 AND i.age <= 30 THEN i.bal ELSE 0 END) AS b7_30,
                   SUM(CASE WHEN i.age > 30 AND i.age <= 60 THEN i.bal ELSE 0 END) AS b30_60,
                   SUM(CASE WHEN i.age > 60 THEN i.bal ELSE 0 END) AS b60
            FROM (
                SELECT inv.customer_id,
                       (inv.total_amount - inv.paid_amount) AS bal,
                       (@asOf - inv.due_date) AS age
                FROM invoices inv
                WHERE inv.tenant_id = @tenantId AND inv.is_deleted = false
                  AND inv.status NOT IN ('Paid', 'Cancelled')
                  AND (inv.total_amount - inv.paid_amount) > 0
            ) i
            JOIN customers c ON c.id = i.customer_id
            GROUP BY i.customer_id, c.name, c.mobile
            ORDER BY total DESC";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@asOf", asOfDate);

        var rows = new List<OutstandingDuesReportDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new OutstandingDuesReportDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7)));
        }
        return rows.ToArray();
    }

    public async Task<AreaPerformanceDto[]> GetAreaWisePerformanceAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        const string sql = @"
            SELECT a.id, COALESCE(a.name, '(no area)') AS area_name,
                   COUNT(DISTINCT o.customer_id) AS customers,
                   COUNT(DISTINCT o.id) AS orders,
                   COALESCE(SUM(oi.quantity * oi.unit_rate), 0) AS revenue
            FROM orders o
            JOIN order_items oi ON oi.order_id = o.id AND oi.tenant_id = @tenantId
            LEFT JOIN areas a ON a.id = o.area_id
            WHERE o.tenant_id = @tenantId AND o.is_deleted = false AND o.status = 'Delivered'
              AND o.order_date BETWEEN @from AND @to
            GROUP BY a.id, a.name
            ORDER BY revenue DESC";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@to", to);

        var rows = new List<AreaPerformanceDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var orders = (int)reader.GetInt64(3);
            var revenue = reader.GetDecimal(4);
            rows.Add(new AreaPerformanceDto(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetString(1),
                (int)reader.GetInt64(2),
                orders,
                revenue,
                orders == 0 ? 0m : Math.Round(revenue / orders, 2)));
        }
        return rows.ToArray();
    }

    public async Task<TopCustomerDto[]> GetTopCustomersAsync(
        Guid tenantId, DateOnly from, DateOnly to, int limit, CancellationToken ct)
    {
        const string sql = @"
            SELECT c.id, c.name, c.mobile,
                   COALESCE(pay.revenue, 0) AS revenue,
                   COALESCE(ord.orders, 0) AS orders,
                   COALESCE(due.outstanding, 0) AS outstanding
            FROM customers c
            LEFT JOIN (SELECT customer_id, SUM(amount) AS revenue FROM payments
                       WHERE tenant_id = @tenantId AND status = 'Completed'
                         AND paid_at::date BETWEEN @from AND @to
                       GROUP BY customer_id) pay ON pay.customer_id = c.id
            LEFT JOIN (SELECT customer_id, COUNT(*) AS orders FROM orders
                       WHERE tenant_id = @tenantId AND is_deleted = false AND status = 'Delivered'
                         AND order_date BETWEEN @from AND @to
                       GROUP BY customer_id) ord ON ord.customer_id = c.id
            LEFT JOIN (SELECT customer_id, SUM(total_amount - paid_amount) AS outstanding FROM invoices
                       WHERE tenant_id = @tenantId AND is_deleted = false
                         AND status NOT IN ('Paid', 'Cancelled')
                       GROUP BY customer_id) due ON due.customer_id = c.id
            WHERE c.tenant_id = @tenantId AND c.is_deleted = false
            ORDER BY COALESCE(pay.revenue, 0) DESC, COALESCE(ord.orders, 0) DESC
            LIMIT @limit";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@to", to);
        cmd.Parameters.AddWithValue("@limit", limit <= 0 ? 20 : limit);

        var rows = new List<TopCustomerDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new TopCustomerDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3),
                (int)reader.GetInt64(4),
                reader.GetDecimal(5)));
        }
        return rows.ToArray();
    }

    public async Task<BottleTrackingReportDto[]> GetBottleTrackingReportAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
            WITH sizes AS (
                SELECT DISTINCT bottle_size FROM products
                WHERE tenant_id = @tenantId AND is_deleted = false
            ),
            mv AS (
                SELECT p.bottle_size AS size,
                       SUM(CASE WHEN m.movement_type = 'Issue'  THEN m.quantity ELSE 0 END) AS issued,
                       SUM(CASE WHEN m.movement_type = 'Return' THEN m.quantity ELSE 0 END) AS returned,
                       SUM(CASE WHEN m.movement_type = 'Damage' THEN m.quantity ELSE 0 END) AS damaged
                FROM inventory_movements m
                JOIN products p ON p.id = m.product_id
                WHERE m.tenant_id = @tenantId
                GROUP BY p.bottle_size
            ),
            st AS (
                SELECT p.bottle_size AS size, SUM(i.total_stock) AS total_stock
                FROM inventory i
                JOIN products p ON p.id = i.product_id
                WHERE i.tenant_id = @tenantId
                GROUP BY p.bottle_size
            )
            SELECT s.bottle_size,
                   COALESCE(st.total_stock, 0) AS total_stock,
                   COALESCE(mv.issued, 0)   AS issued,
                   COALESCE(mv.returned, 0) AS returned,
                   COALESCE(mv.damaged, 0)  AS damaged
            FROM sizes s
            LEFT JOIN mv ON mv.size = s.bottle_size
            LEFT JOIN st ON st.size = s.bottle_size
            ORDER BY s.bottle_size";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);

        var rows = new List<BottleTrackingReportDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var totalStock = (int)reader.GetInt64(1);
            var issued = (int)reader.GetInt64(2);
            var returned = (int)reader.GetInt64(3);
            var damaged = (int)reader.GetInt64(4);
            var missing = issued - returned - damaged;
            rows.Add(new BottleTrackingReportDto(
                reader.GetString(0),
                totalStock, issued, returned, damaged, missing,
                issued == 0 ? 0 : Math.Round(damaged * 100.0 / issued, 2)));
        }
        return rows.ToArray();
    }

    public async Task<CustomerAcquisitionDto[]> GetCustomerAcquisitionAsync(Guid tenantId, int year, CancellationToken ct)
    {
        const string sql = @"
            SELECT g.mon,
                (SELECT COUNT(*) FROM customers c
                   WHERE c.tenant_id = @tenantId AND c.is_deleted = false
                     AND EXTRACT(YEAR FROM c.created_at) = @year
                     AND EXTRACT(MONTH FROM c.created_at) = g.mon) AS new_count,
                (SELECT COUNT(*) FROM customers c
                   WHERE c.tenant_id = @tenantId AND c.is_deleted = false
                     AND c.is_active = false AND c.updated_at IS NOT NULL
                     AND EXTRACT(YEAR FROM c.updated_at) = @year
                     AND EXTRACT(MONTH FROM c.updated_at) = g.mon) AS churned
            FROM generate_series(1, 12) AS g(mon)
            ORDER BY g.mon";

        await using var conn = await OpenAsync(tenantId, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@year", year);

        var rows = new List<CustomerAcquisitionDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new CustomerAcquisitionDto(
                year, reader.GetInt32(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2)));
        }
        return rows.ToArray();
    }
}
