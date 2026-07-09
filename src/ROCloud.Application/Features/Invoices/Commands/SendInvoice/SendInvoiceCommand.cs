using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices.Commands.SendInvoice;

/// <summary>
/// Generates the invoice PDF, stores it, marks the invoice Sent, and emails the link when the
/// customer has an email (and email is enabled). Returns the stored PDF path and whether an email
/// actually went out, so the UI can tell the owner honestly (nothing is sent to a customer with no
/// email on file). WhatsApp delivery is future scope (Phase 14).
/// </summary>
public sealed record SendInvoiceCommand(Guid Id) : IRequest<SendInvoiceResult>;

/// <summary>Outcome of <see cref="SendInvoiceCommand"/> — the stored PDF path and whether it was emailed.</summary>
public sealed record SendInvoiceResult(string PdfPath, bool Emailed);

public class SendInvoiceCommandHandler : IRequestHandler<SendInvoiceCommand, SendInvoiceResult>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IInvoicePdfGenerator _pdf;
    private readonly IFileStorage _storage;
    private readonly IEmailService _email;
    private readonly IEmailBrandContext _brand;
    private readonly INotificationTemplateRenderer _templates;
    private readonly ILogger<SendInvoiceCommandHandler> _logger;
    private readonly IAppSettings _settings;

    public SendInvoiceCommandHandler(
        IAppDbContext db, ITenantContext tenant, IInvoicePdfGenerator pdf, IFileStorage storage,
        IEmailService email, IEmailBrandContext brand, INotificationTemplateRenderer templates,
        ILogger<SendInvoiceCommandHandler> logger, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _pdf = pdf;
        _storage = storage;
        _email = email;
        _brand = brand;
        _templates = templates;
        _logger = logger;
        _settings = settings;
    }

    public async Task<SendInvoiceResult> Handle(SendInvoiceCommand request, CancellationToken ct)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException("Invoice", request.Id);

        var model = await InvoicePdfModelBuilder.BuildAsync(_db, invoice, ct);
        var bytes = _pdf.Generate(model);

        var fileName = $"{invoice.InvoiceNumber}.pdf";
        await using var stream = new MemoryStream(bytes);
        var path = await _storage.UploadAsync(stream, "application/pdf", _tenant.TenantId, "invoices", fileName, ct);

        invoice.PdfUrl = path;
        if (invoice.Status == InvoiceStatus.Draft)
            invoice.Status = InvoiceStatus.Sent;
        await _db.SaveChangesAsync(ct);

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, ct);
        var downloadUrl = _storage.GetDownloadUrl(path, TimeSpan.FromDays(_settings.InvoiceLinkExpiryDays));

        // The PDF is always generated, stored and the invoice marked Sent above; the customer email
        // is gated separately so v1 can keep invoices downloadable while sending nothing out.
        var emailed = false;
        if (_settings.InvoiceSentEnabled && _settings.EmailEnabled && !string.IsNullOrWhiteSpace(customer?.Email))
        {
            var tokens = new Dictionary<string, string>
            {
                ["CustomerName"] = customer!.Name,
                ["InvoiceNumber"] = invoice.InvoiceNumber,
                ["DownloadUrl"] = downloadUrl,
            };
            // Template-driven when a row exists; otherwise the built-in default below.
            var rendered = await _templates.RenderAsync(
                _tenant.TenantId, "invoice_sent", customer.PreferredLanguage, "Email", tokens, ct);
            var subject = rendered?.Subject ?? $"Invoice {invoice.InvoiceNumber}";
            // The PDF now travels as an attachment (below), so the default body references the
            // attachment instead of a link. Tenant overrides may still use {{DownloadUrl}} — that
            // token is kept populated for backward compatibility.
            var body = rendered?.Body
                       ?? $"Hi {customer!.Name}, your invoice {invoice.InvoiceNumber} is attached to this email. Thank you.";

            // Customer-facing mail — brand the email header with the tenant's business, not ROCloud.
            var businessName = await _db.Tenants
                .Where(t => t.Id == _tenant.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(businessName))
                _brand.Current = new EmailBrand(businessName);

            var attachments = new[] { new EmailAttachment(fileName, bytes, "application/pdf") };
            await _email.SendAsync(customer!.Email!, subject, body, attachments, ct);
            emailed = true;
        }

        // TODO (Phase 14 — WhatsApp): send the invoice link via MSG91/WhatsApp to the customer's mobile.
        _logger.LogInformation(
            "TODO[Phase14]: WhatsApp invoice {InvoiceNumber} to {Mobile}", invoice.InvoiceNumber, customer?.Mobile);

        return new SendInvoiceResult(path, emailed);
    }
}
