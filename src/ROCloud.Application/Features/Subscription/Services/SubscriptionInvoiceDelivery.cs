using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Features.Subscription.Services;

/// <summary>
/// Default <see cref="ISubscriptionInvoiceDelivery"/>: QuestPDF render → IFileStorage (folder
/// "subscription-invoices") → owner email (ROCloud-branded, honouring Notifications:EmailEnabled).
/// Every step is wrapped so a PDF/email failure never aborts the caller's transaction.
/// </summary>
public class SubscriptionInvoiceDelivery : ISubscriptionInvoiceDelivery
{
    private const string Folder = "subscription-invoices";

    private readonly ISubscriptionInvoicePdfGenerator _pdf;
    private readonly IFileStorage _storage;
    private readonly IEmailService _email;
    private readonly INotificationTemplateRenderer _templates;
    private readonly IAppSettings _settings;
    private readonly ILogger<SubscriptionInvoiceDelivery> _logger;

    public SubscriptionInvoiceDelivery(
        ISubscriptionInvoicePdfGenerator pdf, IFileStorage storage, IEmailService email,
        INotificationTemplateRenderer templates, IAppSettings settings,
        ILogger<SubscriptionInvoiceDelivery> logger)
    {
        _pdf = pdf;
        _storage = storage;
        _email = email;
        _templates = templates;
        _settings = settings;
        _logger = logger;
    }

    public async Task IssueAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default)
    {
        try
        {
            var (path, bytes) = await RenderAndStoreAsync(invoice, tenant, paid: false, ct);
            invoice.PdfUrl = path;

            if (!_settings.EmailEnabled || string.IsNullOrWhiteSpace(tenant.OwnerEmail))
                return;

            var payUrl = $"{_settings.TenantUrlFormat.Replace("{subdomain}", tenant.Subdomain)}/settings/subscription";
            var tokens = new Dictionary<string, string>
            {
                ["TenantName"] = tenant.Name,
                ["InvoiceNumber"] = invoice.InvoiceNumber,
                ["PlanName"] = $"{invoice.PlanType} plan",
                ["Amount"] = invoice.Amount.ToString("N2"),
                ["DueDate"] = invoice.DueDate.ToString("dd MMM yyyy"),
                ["PayUrl"] = payUrl,
            };
            var rendered = await _templates.RenderAsync(null, "subscription_invoice", tenant.DefaultLanguage, "Email", tokens, ct);
            var subject = rendered?.Subject ?? $"Your ROCloud invoice {invoice.InvoiceNumber}";
            var body = rendered?.Body ??
                $"Hi {tenant.Name}, your ROCloud subscription invoice {invoice.InvoiceNumber} for " +
                $"₹{invoice.Amount:N2} is attached. Please pay by {invoice.DueDate:dd MMM yyyy} to keep your " +
                $"service active. Pay here: {payUrl}";

            await _email.SendAsync(tenant.OwnerEmail, subject, body, Attach(invoice, bytes), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SubscriptionInvoiceDelivery: issue failed for invoice {Invoice}", invoice.InvoiceNumber);
        }
    }

    public async Task ReceiptAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default)
    {
        try
        {
            var (path, bytes) = await RenderAndStoreAsync(invoice, tenant, paid: true, ct);
            invoice.PdfUrl = path;

            if (!_settings.EmailEnabled || string.IsNullOrWhiteSpace(tenant.OwnerEmail))
                return;

            var subject = $"Payment received — ROCloud invoice {invoice.InvoiceNumber}";
            var body =
                $"Hi {tenant.Name}, we've received your payment of ₹{invoice.Amount:N2} for invoice " +
                $"{invoice.InvoiceNumber}. Your ROCloud subscription is active. Thank you!";

            await _email.SendAsync(tenant.OwnerEmail, subject, body, Attach(invoice, bytes), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SubscriptionInvoiceDelivery: receipt failed for invoice {Invoice}", invoice.InvoiceNumber);
        }
    }

    public async Task StorePdfAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default)
    {
        try
        {
            var (path, _) = await RenderAndStoreAsync(
                invoice, tenant, paid: invoice.Status == SubscriptionInvoiceStatus.Paid, ct);
            invoice.PdfUrl = path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SubscriptionInvoiceDelivery: store-pdf failed for invoice {Invoice}", invoice.InvoiceNumber);
        }
    }

    private async Task<(string Path, byte[] Bytes)> RenderAndStoreAsync(
        SubscriptionInvoice invoice, Tenant tenant, bool paid, CancellationToken ct)
    {
        var model = new SubscriptionInvoicePdfModel(
            invoice.InvoiceNumber,
            DateOnly.FromDateTime(DateTime.UtcNow),
            invoice.PeriodStart, invoice.PeriodEnd,
            invoice.PlanType, invoice.BillingCycle,
            invoice.Description ?? $"{invoice.PlanType} plan",
            invoice.GrossAmount, invoice.DiscountAmount, invoice.Amount,
            paid, tenant.Name, tenant.GstNumber);

        var bytes = _pdf.Generate(model);
        await using var stream = new MemoryStream(bytes);
        var path = await _storage.UploadAsync(
            stream, "application/pdf", tenant.Id, Folder, $"{invoice.InvoiceNumber}.pdf", ct);
        return (path, bytes);
    }

    private static IReadOnlyList<EmailAttachment> Attach(SubscriptionInvoice invoice, byte[] bytes) =>
        new[] { new EmailAttachment($"{invoice.InvoiceNumber}.pdf", bytes, "application/pdf") };
}
