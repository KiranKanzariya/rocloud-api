using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Customers;

/// <summary>
/// How many returnable jars of one product a customer is holding, and the guard that stops more than
/// that coming back.
///
/// <para>Without the guard a return of any size succeeded for anyone. That is not a harmless
/// over-count: <c>GetCustomerJarBalance</c> filters to <c>Outstanding &gt; 0</c>, so an over-return
/// makes the product VANISH from the customer's jar balance rather than showing as negative — while
/// <c>Inventory.IssuedStock</c> quietly goes below zero. The plant's float and the deposit figures are
/// then wrong with nothing on any screen to say so.</para>
///
/// <para>The count deliberately matches <c>GetCustomerJarBalanceQuery</c> line for line. If the two
/// ever disagree, a screen offers a number the API then refuses, which is worse than either rule on
/// its own.</para>
/// </summary>
internal static class CustomerJarHoldings
{
    /// <summary>Σ(Issue) − Σ(Return + customer-scoped Damage) for one customer and product.</summary>
    public static Task<int> CountAsync(
        IAppDbContext db, Guid customerId, Guid productId, CancellationToken ct)
        => db.InventoryMovements
            .Where(m => m.CustomerId == customerId
                        && m.ProductId == productId
                        && (m.MovementType == InventoryMovementType.Issue
                            || m.MovementType == InventoryMovementType.Return
                            || m.MovementType == InventoryMovementType.Damage))
            .SumAsync(m => m.MovementType == InventoryMovementType.Issue ? m.Quantity : -m.Quantity, ct);

    /// <summary>
    /// Throws when more jars are coming back than the customer holds.
    ///
    /// <para>The message names the number allowed and points at the opening balance, because the
    /// legitimate version of this — a jar issued before ROCloud existed, so its Issue was never
    /// recorded — is exactly what the opening balance is for. Telling the owner only "invalid" would
    /// leave them unable to record a real return.</para>
    /// </summary>
    /// <param name="field">The request field to hang the error on, so the form marks the right input.</param>
    public static async Task EnsureCanReturnAsync(
        IAppDbContext db, Guid customerId, Guid productId, int quantity,
        string field, CancellationToken ct)
    {
        var held = await CountAsync(db, customerId, productId, ct);
        if (quantity <= held) return;

        var name = await db.Customers
            .Where(c => c.Id == customerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? "This customer";

        throw new ValidationException(new Dictionary<string, string[]>
        {
            [field] = held == 0
                ? [$"{name} is not holding any of these jars. If these are older jars, set an opening balance first."]
                : [$"{name} is only holding {held}. If more are coming back, set an opening balance first."],
        });
    }
}
