using System.Globalization;

namespace ROCloud.Application.Common;

/// <summary>
/// Builds the <c>upi://pay</c> deep link that becomes the scan-to-pay QR on a customer invoice or
/// statement (guide §10). Pure string work — the QR image itself is drawn in Infrastructure, so this
/// layer stays free of any encoding library and the payload is directly testable.
///
/// The money goes straight to the tenant's own UPI id: ROCloud never sees the transfer and cannot
/// reconcile it, so <c>tn</c> carries the invoice number purely so the OWNER can match the credit in
/// their UPI app when they come to record the payment.
/// </summary>
public static class UpiPaymentLink
{
    /// <summary>A UPI transaction note is short; keep well inside what PSP apps will accept.</summary>
    private const int MaxNoteLength = 50;

    /// <summary>
    /// Builds the note that identifies a payment to the OWNER when it lands in their UPI app.
    ///
    /// <para>Without one, a scan produces "₹25 received" from an unfamiliar handle and the owner has
    /// no way to tell whose it was — which is fatal for a business whose customers all pay the same
    /// small amounts. Reference first (invoice number, or customer code), name second, so the useful
    /// part survives if an app truncates the display.</para>
    /// </summary>
    public static string? Reference(string? reference, string? customerName) =>
        Note(string.Join(' ', new[] { reference, customerName }.Where(s => !string.IsNullOrWhiteSpace(s))));

    /// <summary>
    /// The deep link, or <c>null</c> when there is nothing to collect — no id configured, or an amount
    /// of zero or less.
    /// </summary>
    /// <param name="amount">
    /// What to ask for: the OUTSTANDING balance, not the invoice total. A part-paid invoice
    /// re-downloaded later must request what is still owed, and a settled one must produce no QR at
    /// all rather than invite a second payment.
    /// </param>
    public static string? Build(string? vpa, string? payeeName, decimal amount, string? reference)
    {
        if (string.IsNullOrWhiteSpace(vpa)) return null;
        if (amount <= 0m) return null;

        // Invariant, always: the API serves hi/gu cultures via RequestLocalization, and a culture with
        // a comma decimal separator would emit am=225,00 — which UPI apps reject or misread.
        var am = amount.ToString("0.00", CultureInfo.InvariantCulture);

        var query = new List<string>
        {
            $"pa={Uri.EscapeDataString(vpa.Trim())}",
            $"pn={Uri.EscapeDataString(Payee(payeeName))}",
            $"am={am}",
            "cu=INR"
        };

        var note = Note(reference);
        if (note is not null) query.Add($"tn={Uri.EscapeDataString(note)}");

        return "upi://pay?" + string.Join('&', query);
    }

    /// <summary>`pn` is required by the UPI spec, so fall back rather than omit it.</summary>
    private static string Payee(string? payeeName) =>
        string.IsNullOrWhiteSpace(payeeName) ? "Merchant" : payeeName.Trim();

    /// <summary>
    /// A note every UPI app will accept: letters, digits, space, hyphen, dot and slash only, runs of
    /// whitespace collapsed, truncated on a word boundary where possible.
    ///
    /// <para>Payer apps vary in what they tolerate in <c>tn</c>, and a rejected note can take the whole
    /// intent down rather than just dropping the text — so anything outside that set is stripped rather
    /// than escaped. Customer names here are free text typed by the owner, so this WILL see commas,
    /// ampersands and emoji.</para>
    /// </summary>
    private static string? Note(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        var sb = new System.Text.StringBuilder(reference.Length);
        var lastWasSpace = false;
        foreach (var ch in reference)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '.' or '/')
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(ch) && sb.Length > 0 && !lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        var cleaned = sb.ToString().TrimEnd();
        if (cleaned.Length == 0) return null;
        if (cleaned.Length <= MaxNoteLength) return cleaned;

        // Prefer cutting at the last space so the note ends on a whole word, not mid-name.
        var cut = cleaned[..MaxNoteLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > MaxNoteLength / 2 ? cut[..lastSpace] : cut).TrimEnd();
    }
}
