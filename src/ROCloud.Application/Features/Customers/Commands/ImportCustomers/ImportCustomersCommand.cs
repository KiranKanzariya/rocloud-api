using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Commands.CreateCustomer;
using ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;
using ROCloud.Application.Features.CustomerSubscriptions.Commands.CreateCustomerSubscription;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Customers.Commands.ImportCustomers;

/// <summary>Outcome of one CSV row.</summary>
public sealed record ImportRowResultDto(int Row, string? Mobile, string? Name, string Status, string? Message);

/// <summary>Summary of a customer import (or its dry-run preview).</summary>
public sealed record ImportCustomersResultDto(
    bool DryRun, int Total, int Created, int Skipped, int Failed, IReadOnlyList<ImportRowResultDto> Rows);

/// <summary>
/// Bulk-imports customers from the migration CSV (spec: steps 3–5). When <paramref name="DryRun"/> is
/// true it validates and previews only (no writes). On commit each row becomes a Customer + optional
/// opening balance (jars/dues) + optional subscription, reusing the existing commands. Existing mobiles
/// (and in-file duplicates) are skipped, so re-running the same file is safe.
/// </summary>
public sealed record ImportCustomersCommand(string Csv, bool DryRun, DateOnly CutoverDate)
    : IRequest<ImportCustomersResultDto>;

public class ImportCustomersCommandHandler : IRequestHandler<ImportCustomersCommand, ImportCustomersResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenant;

    public ImportCustomersCommandHandler(IAppDbContext db, IMediator mediator, ITenantContext tenant)
    {
        _db = db;
        _mediator = mediator;
        _tenant = tenant;
    }

    private static readonly HashSet<string> DeliveryModes = new(Enum.GetNames<DeliveryMode>(), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PaymentPreferences = new(Enum.GetNames<PaymentPreference>(), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Frequencies = new(Enum.GetNames<SubscriptionFrequency>(), StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> SizeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["18l"] = "18L", ["20l"] = "20L", ["250ml"] = "250ml", ["500ml"] = "500ml", ["1l"] = "1L", ["custom"] = "Custom",
    };

    public async Task<ImportCustomersResultDto> Handle(ImportCustomersCommand request, CancellationToken ct)
    {
        var rows = CsvReader.Parse(request.Csv ?? string.Empty);

        // Reference data, loaded once. Existing mobiles are canonicalised the same way as the CSV
        // values so dedupe still catches legacy rows stored in a non-+91 form (e.g. bare 10-digit).
        var existing = await _db.Customers.Select(c => new { c.Mobile, c.Name }).ToListAsync(ct);
        var existingMobiles = existing.Select(c => NormalizeMobile(c.Mobile))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        var existingNames = existing.Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var areasByName = (await _db.Areas.Select(a => new { a.Id, a.Name }).ToListAsync(ct))
            .GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var productBySize = (await _db.Products.Select(p => new { p.Id, p.BottleSize }).ToListAsync(ct))
            .GroupBy(p => p.BottleSize.ToWire(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var seenMobiles = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ImportRowResultDto>(rows.Count);
        int created = 0, skipped = 0, failed = 0;

        // Import only fills up to the tenant's remaining customer headroom (guide §25); the rest is skipped.
        var remaining = await Subscription.PlanLimits.CustomerHeadroomAsync(_db, _tenant, ct);

        for (var i = 0; i < rows.Count; i++)
        {
            var rowNo = i + 2; // 1-based + header row
            var row = rows[i];
            var name = Get(row, "name");
            var rawMobile = Get(row, "mobile");
            var mobile = NormalizeMobile(rawMobile); // null when blank OR unparseable

            var parsed = Parse(row, mobile, areasByName, productBySize);

            if (parsed.Error is { } err)
            {
                results.Add(new ImportRowResultDto(rowNo, mobile, name, "Failed", err));
                failed++;
                continue;
            }

            // Name is required; mobile is optional (an owner may not have every customer's number).
            if (string.IsNullOrWhiteSpace(name))
            {
                results.Add(new ImportRowResultDto(rowNo, mobile, name, "Failed", "Name is required."));
                failed++;
                continue;
            }

            // A mobile that was provided but couldn't be parsed is an error (don't silently drop it).
            if (!string.IsNullOrWhiteSpace(rawMobile) && mobile is null)
            {
                results.Add(new ImportRowResultDto(rowNo, rawMobile, name, "Failed", "Invalid mobile number."));
                failed++;
                continue;
            }

            // Mobile dedupe applies only when a mobile is present; mobile-less rows fall back to name dedupe.
            if (mobile is not null && (existingMobiles.Contains(mobile) || !seenMobiles.Add(mobile)))
            {
                results.Add(new ImportRowResultDto(rowNo, mobile, name, "Skipped", "A customer with this mobile already exists."));
                skipped++;
                continue;
            }

            var nameKey = name.Trim();
            if (existingNames.Contains(nameKey) || !seenNames.Add(nameKey))
            {
                results.Add(new ImportRowResultDto(rowNo, mobile, name, "Skipped", "A customer with this name already exists."));
                skipped++;
                continue;
            }

            // Stop once the plan's customer cap is reached — skip the rest (applies to dry-run too).
            if (remaining <= 0)
            {
                results.Add(new ImportRowResultDto(rowNo, mobile, name, "Skipped",
                    "Your plan's customer limit is reached — upgrade to add more."));
                skipped++;
                continue;
            }
            remaining--;

            var warning = parsed.Warnings.Count > 0 ? string.Join(" ", parsed.Warnings) : null;

            if (request.DryRun)
            {
                results.Add(new ImportRowResultDto(rowNo, mobile, name, "Created", warning));
                created++;
                continue;
            }

            // Commit: customer first, then opening balance + subscription (best-effort, reported).
            Guid customerId;
            try
            {
                customerId = await _mediator.Send(new CreateCustomerCommand(
                    parsed.AreaId, name, mobile, NullIfBlank(Get(row, "alternate_mobile")), NullIfBlank(Get(row, "email")),
                    NullIfBlank(Get(row, "address_line")), NullIfBlank(Get(row, "landmark")), null, null,
                    parsed.DeliveryMode, parsed.PaymentPreference, parsed.PreferredBottleSize,
                    NullIfBlank(Get(row, "preferred_language")), NullIfBlank(Get(row, "notes")),
                    AllowMissingMobile: true), ct);
            }
            catch (ValidationException ex)
            {
                results.Add(new ImportRowResultDto(rowNo, mobile, name, "Failed", Flatten(ex)));
                failed++;
                continue;
            }

            var warnings = new List<string>(parsed.Warnings);

            if (parsed.Jars.Count > 0 || parsed.OpeningDues != 0)
            {
                try
                {
                    await _mediator.Send(new SetCustomerOpeningBalanceCommand(
                        customerId, request.CutoverDate, parsed.Jars, parsed.OpeningDues, "Imported from book"), ct);
                }
                catch (Exception ex) { warnings.Add($"Opening balance skipped: {Message(ex)}"); }
            }

            if (parsed.Subscription is { } sub)
            {
                try
                {
                    await _mediator.Send(new CreateCustomerSubscriptionCommand(
                        customerId, sub.ProductId, sub.Quantity, sub.Frequency, sub.Rate, sub.StartDate), ct);
                }
                catch (Exception ex) { warnings.Add($"Subscription skipped: {Message(ex)}"); }
            }

            results.Add(new ImportRowResultDto(rowNo, mobile, name, "Created",
                warnings.Count > 0 ? string.Join(" ", warnings) : null));
            created++;
        }

        return new ImportCustomersResultDto(request.DryRun, rows.Count, created, skipped, failed, results);
    }

    private sealed class ParsedRow
    {
        public string? Error;
        public readonly List<string> Warnings = new();
        public Guid? AreaId;
        public string DeliveryMode = "HomeDelivery";
        public string PaymentPreference = "PerBottle";
        public string? PreferredBottleSize;
        public List<OpeningJarInputDto> Jars = new();
        public decimal OpeningDues;
        public ParsedSub? Subscription;
    }

    private sealed record ParsedSub(Guid ProductId, int Quantity, string Frequency, decimal? Rate, DateOnly? StartDate);

    /// <summary>Shared validation/resolution used for both the dry-run preview and the commit pass.</summary>
    private ParsedRow Parse(
        Dictionary<string, string> row, string? mobile,
        Dictionary<string, Guid> areasByName, Dictionary<string, Guid> productBySize)
    {
        var p = new ParsedRow();

        var deliveryMode = Get(row, "delivery_mode");
        if (!string.IsNullOrWhiteSpace(deliveryMode))
        {
            if (!DeliveryModes.Contains(deliveryMode)) return Fail(p, $"Invalid delivery_mode '{deliveryMode}'.");
            p.DeliveryMode = deliveryMode;
        }

        var paymentPref = Get(row, "payment_preference");
        if (!string.IsNullOrWhiteSpace(paymentPref))
        {
            if (!PaymentPreferences.Contains(paymentPref)) return Fail(p, $"Invalid payment_preference '{paymentPref}'.");
            p.PaymentPreference = paymentPref;
        }

        var bottle = Get(row, "preferred_bottle_size");
        if (!string.IsNullOrWhiteSpace(bottle))
        {
            if (!SizeTokens.TryGetValue(bottle, out var wire)) return Fail(p, $"Invalid preferred_bottle_size '{bottle}'.");
            p.PreferredBottleSize = wire;
        }

        var area = Get(row, "area");
        if (!string.IsNullOrWhiteSpace(area))
        {
            if (areasByName.TryGetValue(area, out var areaId)) p.AreaId = areaId;
            else p.Warnings.Add($"Area '{area}' not found — left unassigned.");
        }

        // Opening jars: any "opening_jars_<size>" column.
        foreach (var (key, value) in row)
        {
            if (!key.StartsWith("opening_jars_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value)) continue;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
                return Fail(p, $"'{key}' must be a whole number.");
            if (qty <= 0) continue;

            var token = key["opening_jars_".Length..];
            if (!SizeTokens.TryGetValue(token, out var wire) || !productBySize.TryGetValue(wire, out var pid))
            { p.Warnings.Add($"No product for '{key}' — {qty} jar(s) skipped."); continue; }
            p.Jars.Add(new OpeningJarInputDto(pid, qty));
        }

        var dues = Get(row, "opening_dues");
        if (!string.IsNullOrWhiteSpace(dues))
        {
            if (!decimal.TryParse(dues, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return Fail(p, $"opening_dues '{dues}' is not a number.");
            p.OpeningDues = d;
        }

        ParseSubscription(row, productBySize, p);
        return p;
    }

    private void ParseSubscription(Dictionary<string, string> row, Dictionary<string, Guid> productBySize, ParsedRow p)
    {
        var size = Get(row, "sub_product_size");
        var qtyRaw = Get(row, "sub_qty");
        if (string.IsNullOrWhiteSpace(size) && string.IsNullOrWhiteSpace(qtyRaw)) return; // no subscription on this row

        if (string.IsNullOrWhiteSpace(size) || !SizeTokens.TryGetValue(size, out var wire) || !productBySize.TryGetValue(wire, out var pid))
        { p.Warnings.Add($"Subscription skipped — no product for size '{size}'."); return; }

        if (!int.TryParse(qtyRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
        { p.Warnings.Add("Subscription skipped — sub_qty must be a positive whole number."); return; }

        var freq = Get(row, "sub_frequency");
        if (string.IsNullOrWhiteSpace(freq)) freq = "Daily";
        else if (!Frequencies.Contains(freq)) { p.Warnings.Add($"Subscription skipped — invalid sub_frequency '{freq}'."); return; }

        decimal? rate = null;
        var rateRaw = Get(row, "sub_rate");
        if (!string.IsNullOrWhiteSpace(rateRaw))
        {
            if (!decimal.TryParse(rateRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var r))
            { p.Warnings.Add($"Subscription skipped — sub_rate '{rateRaw}' is not a number."); return; }
            rate = r;
        }

        DateOnly? start = null;
        var startRaw = Get(row, "sub_start_date");
        if (!string.IsNullOrWhiteSpace(startRaw))
        {
            if (!DateOnly.TryParse(startRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            { p.Warnings.Add($"Subscription skipped — sub_start_date '{startRaw}' is not a valid date."); return; }
            start = d;
        }

        p.Subscription = new ParsedSub(pid, qty, freq, rate, start);
    }

    private static ParsedRow Fail(ParsedRow p, string error) { p.Error = error; return p; }

    private static string Get(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) ? v : string.Empty;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Canonicalises to the stored "+91XXXXXXXXXX" form (what CreateCustomer validates and the DB holds,
    /// so dedupe matches). Tolerates a +91 / 91 / leading-0 prefix; requires a final 10 digits.
    /// </summary>
    private static string? NormalizeMobile(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is 11 or 12 && (digits.StartsWith("91") || digits.StartsWith("0")))
            digits = digits[^10..];
        return digits.Length == 10 ? "+91" + digits : null;
    }

    private static string Flatten(ValidationException ex) =>
        string.Join(" ", ex.Errors.SelectMany(e => e.Value));

    private static string Message(Exception ex) =>
        ex is ValidationException v ? Flatten(v) : ex.Message;
}
