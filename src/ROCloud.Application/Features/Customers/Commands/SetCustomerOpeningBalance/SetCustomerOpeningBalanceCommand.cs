using ROCloud.Application.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;

/// <summary>One product's empties a customer already holds at migration cutover.</summary>
public sealed record OpeningJarInputDto(Guid ProductId, int Quantity);

/// <summary>Request body for the opening-balance endpoint (customer id comes from the route).</summary>
public sealed record SetCustomerOpeningBalanceRequest(
    DateOnly CutoverDate,
    IReadOnlyList<OpeningJarInputDto>? Jars,
    decimal OpeningDues,
    string? Note);

/// <summary>
/// Seeds a customer's starting state when migrating from a paper book (spec: "Opening balance helper").
/// As of <paramref name="CutoverDate"/> it records, in one transaction:
///   • the empties they hold  → customer-scoped <c>Issue</c> inventory movements (drive the jar balance),
///   • money they owe (Dues &gt; 0) → one "opening balance" invoice (drives the money balance),
///   • an advance (Dues &lt; 0)     → one credit payment.
/// Every row it creates is tagged in Notes with <see cref="Marker"/> so it can be detected (to block
/// a second run) and later cleared/excluded from real-sales reporting. It is NOT a priced sale, so the
/// invoice carries no GST/discount and no billing period.
/// </summary>
public sealed record SetCustomerOpeningBalanceCommand(
    Guid CustomerId,
    DateOnly CutoverDate,
    IReadOnlyList<OpeningJarInputDto> Jars,
    decimal OpeningDues,
    string? Note) : IRequest
{
    /// <summary>Prefix written to the Notes of every record this command creates, for detection/cleanup.</summary>
    public const string Marker = "[opening-balance]";
}

public class SetCustomerOpeningBalanceCommandValidator : AbstractValidator<SetCustomerOpeningBalanceCommand>
{
    public SetCustomerOpeningBalanceCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.CutoverDate)
            .LessThanOrEqualTo(_ => AppTimeZone.Today(DateTime.UtcNow))
            .WithMessage("The cutover date cannot be in the future.");
        RuleForEach(c => c.Jars).ChildRules(j =>
        {
            j.RuleFor(x => x.ProductId).NotEmpty();
            j.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public class SetCustomerOpeningBalanceCommandHandler : IRequestHandler<SetCustomerOpeningBalanceCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;

    public SetCustomerOpeningBalanceCommandHandler(
        IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public async Task Handle(SetCustomerOpeningBalanceCommand request, CancellationToken ct)
    {
        // Tenant query filter + explicit id → cross-tenant access yields NotFound (404).
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        await GuardNotAlreadySetAsync(customer.Id, ct);

        // Reject jars for products that don't belong to this tenant (don't silently drop them).
        var productIds = request.Jars.Select(j => j.ProductId).Distinct().ToList();
        if (productIds.Count > 0)
        {
            var known = await _db.Products.Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
            var missing = productIds.Except(known).ToList();
            if (missing.Count > 0)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["jars"] = ["One or more products do not exist in this tenant."]
                });
        }

        var note = BuildNote(request.Note);

        // 1) Empties held → one Issue movement per product, scoped to the customer.
        foreach (var jar in request.Jars.Where(j => j.Quantity > 0))
            await RecordOpeningIssueAsync(jar.ProductId, jar.Quantity, customer.Id, note, ct);

        // 2) Money: dues → opening invoice; advance → credit payment.
        Invoice? openingInvoice = null;
        if (request.OpeningDues > 0)
            openingInvoice = await AddOpeningInvoiceAsync(customer.Id, request.OpeningDues, request.CutoverDate, note, ct);
        else if (request.OpeningDues < 0)
            AddAdvancePayment(customer.Id, -request.OpeningDues, request.CutoverDate, note);

        await SaveSeededRowsAsync(customer.Id, openingInvoice, request.CutoverDate, ct);

        // An imported advance must immediately settle the customer's oldest dues; an opening invoice
        // must be settled by any advance they already hold. Either way the invoices need re-stating.
        await Payments.InvoiceAllocationSync.SyncAsync(_db, customer.Id, ct);
    }

    /// <summary>An opening balance is a one-time seed; block a second run (clear it first to redo).</summary>
    private async Task GuardNotAlreadySetAsync(Guid customerId, CancellationToken ct)
    {
        var m = SetCustomerOpeningBalanceCommand.Marker;
        var already =
            await _db.InventoryMovements.AnyAsync(x => x.CustomerId == customerId && x.Notes != null && x.Notes.StartsWith(m), ct)
            || await _db.Invoices.AnyAsync(x => x.CustomerId == customerId && x.Notes != null && x.Notes.StartsWith(m), ct)
            || await _db.Payments.AnyAsync(x => x.CustomerId == customerId && x.Notes != null && x.Notes.StartsWith(m), ct);

        if (already)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["openingBalance"] = ["An opening balance has already been set for this customer. Clear it before setting a new one."]
            });
    }

    private async Task RecordOpeningIssueAsync(Guid productId, int quantity, Guid customerId, string note, CancellationToken ct)
    {
        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.ProductId == productId, ct);
        if (inv is null)
        {
            inv = new Domain.Entities.Tenant.Inventory
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                ProductId = productId,
                LastUpdated = DateTime.UtcNow
            };
            _db.Inventories.Add(inv);
        }

        InventoryMath.Apply(inv, InventoryMovementType.Issue, quantity);

        _db.InventoryMovements.Add(new InventoryMovement
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ProductId = productId,
            CustomerId = customerId,
            MovementType = InventoryMovementType.Issue,
            Quantity = quantity,
            PerformedBy = _currentUser.UserId,
            Notes = note
        });
    }

    /// <summary>
    /// Persists the seeded rows, resilient to the two ways a concurrent write can clash on save:
    ///   • the invoice number we minted was taken between read and save → re-mint above the new
    ///     high-water mark and retry (bounded, so a persistent fault still surfaces);
    ///   • this customer's opening balance was seeded by another request → raise the guard's clean 400.
    /// Anything else is a genuine fault and propagates unchanged — we never mask one as "already set".
    /// </summary>
    private async Task SaveSeededRowsAsync(Guid customerId, Invoice? openingInvoice, DateOnly cutover, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (openingInvoice is not null
                                               && attempt < 5
                                               && InvoiceNumberGenerator.IsDuplicateNumber(ex))
            {
                openingInvoice.InvoiceNumber =
                    await InvoiceNumberGenerator.NextAsync(_db, _tenant.TenantId, cutover, ct);
            }
            catch (DbUpdateException)
            {
                await GuardNotAlreadySetAsync(customerId, ct); // → clean 400 if already seeded
                throw;                                          // otherwise a genuine fault
            }
        }
    }

    private async Task<Invoice> AddOpeningInvoiceAsync(Guid customerId, decimal amount, DateOnly cutover, string note, CancellationToken ct)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = customerId,
            InvoiceNumber = await NextInvoiceNumberAsync(cutover, ct),
            InvoiceDate = cutover,
            DueDate = cutover,
            PeriodFrom = null,           // not a billing period → never "covers" real delivered orders
            PeriodTo = null,
            SubTotal = amount,
            TaxAmount = 0m,              // carried-forward balance, not a priced sale
            Discount = 0m,
            TotalAmount = amount,
            PaidAmount = 0m,
            Status = InvoiceStatus.Sent, // counts toward the customer's owed balance
            Notes = note
        };
        _db.Invoices.Add(invoice);
        return invoice;
    }

    private void AddAdvancePayment(Guid customerId, decimal amount, DateOnly cutover, string note)
        => _db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = customerId,
            InvoiceId = null,
            Amount = amount,
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentStatus.Completed,
            CollectedBy = _currentUser.UserId,
            PaidAt = cutover.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Notes = note
        });

    private static string BuildNote(string? userNote)
    {
        var marker = SetCustomerOpeningBalanceCommand.Marker;
        var text = string.IsNullOrWhiteSpace(userNote) ? "Carried forward from book" : userNote.Trim();
        return $"{marker} {text}";
    }

    private Task<string> NextInvoiceNumberAsync(DateOnly invoiceDate, CancellationToken ct)
        => InvoiceNumberGenerator.NextAsync(_db, _tenant.TenantId, invoiceDate, ct);
}
