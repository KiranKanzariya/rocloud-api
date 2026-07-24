namespace ROCloud.Application.Features.Payments;

/// <summary>
/// System-generated payment notes, and the marker that lets a client tell "the collector wrote
/// something" apart from "the platform needs you to act on this".
///
/// The duplicate-payment note means real money was captured twice and the customer may be owed a
/// refund. Matching on prose would break the moment the wording or language changed, so the note
/// carries a stable <see cref="ActionRequiredMarker"/> prefix instead.
/// </summary>
public static class PaymentNotes
{
    /// <summary>
    /// Prefix on any note the owner must act on. Kept out of the UI text — the portal strips it and
    /// renders the row as a warning.
    /// </summary>
    public const string ActionRequiredMarker = "[!]";

    /// <summary>Money captured against an invoice that was already settled — a refund may be due.</summary>
    public const string PossibleDuplicate =
        ActionRequiredMarker + " Possible duplicate payment: the invoice was already settled. A refund may be due.";

    /// <summary>Checkout was started but never paid; the pending row was closed off. No action needed.</summary>
    public const string CheckoutAbandoned = "Reconciled: no payment received — checkout abandoned.";

    /// <summary>True when this note needs the owner's attention rather than being a plain remark.</summary>
    public static bool IsActionRequired(string? notes) =>
        notes is not null && notes.Contains(ActionRequiredMarker, StringComparison.Ordinal);

    /// <summary>Appends a note, preserving anything the collector already wrote.</summary>
    public static string Append(string? notes, string add) =>
        string.IsNullOrWhiteSpace(notes) ? add : $"{notes} {add}";
}
