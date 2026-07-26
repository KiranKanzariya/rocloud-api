using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Exceptions;

namespace ROCloud.Application.Features.Orders;

/// <summary>
/// The always-on protection for orders backdated into a period that has already been invoiced. Such an
/// order would otherwise fall between the invoice that already ran and the next one — never billed, no
/// error (see the discussion behind Billing:BackdateWindowDays). Rejecting it keeps closed invoices
/// self-consistent; the owner re-enters it dated today so it lands on the next invoice. This is separate
/// from the day-count window (BackdateGuard) and fires regardless of how far back the date is.
/// </summary>
internal static class BackdatedOrderGuard
{
    public static async Task EnsureNotInBilledPeriodAsync(
        IAppDbContext db, Guid customerId, DateOnly orderDate, CancellationToken ct)
    {
        var billed = await db.Invoices.AnyAsync(
            i => i.CustomerId == customerId && i.PeriodFrom <= orderDate && i.PeriodTo >= orderDate, ct);

        if (billed)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["orderDate"] =
                [
                    $"{orderDate:MMMM yyyy} is already invoiced for this customer, so this order can't be added " +
                    "to it. Enter it dated today and it will appear on the next invoice."
                ]
            });
    }
}
