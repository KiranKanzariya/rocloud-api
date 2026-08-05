using System.Globalization;
using ROCloud.Application.Common;

namespace ROCloud.Application.Tests.Payments;

/// <summary>
/// The scan-to-pay payload (guide §10). This string becomes a QR a real customer scans with a real
/// banking app, so the failure modes are money-shaped: an amount their app misreads, a payee id that
/// silently loses characters, or a QR offered on an invoice that is already settled.
/// </summary>
public class UpiPaymentLinkTests
{
    private const string Vpa = "dabhiro@okaxis";

    [Fact]
    public void BuildsTheDeepLink_WithAmountPayeeAndInvoiceReference()
    {
        var link = UpiPaymentLink.Build(Vpa, "Dabhi RO Water", 225m, "INV-202608-0012");

        Assert.Equal(
            "upi://pay?pa=dabhiro%40okaxis&pn=Dabhi%20RO%20Water&am=225.00&cu=INR&tn=INV-202608-0012",
            link);
    }

    [Fact]
    public void AmountIsInvariant_EvenUnderACommaDecimalCulture()
    {
        // The API serves hi/gu via RequestLocalization, and a comma-decimal culture would emit
        // "am=225,00" — which UPI apps reject or misread. This is the bug this test exists to stop.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // 225,00
            var link = UpiPaymentLink.Build(Vpa, "Shop", 225.5m, null);
            Assert.Contains("am=225.50", link);
            Assert.DoesNotContain("225,50", link);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public void NoLink_WhenThereIsNothingLeftToPay(decimal amount)
    {
        // A settled or cancelled invoice must not carry a QR — otherwise a re-sent PDF invites a
        // second payment for money already received.
        Assert.Null(UpiPaymentLink.Build(Vpa, "Shop", amount, "INV-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoLink_WhenTheTenantHasNoUpiId(string? vpa)
        => Assert.Null(UpiPaymentLink.Build(vpa, "Shop", 225m, "INV-1"));

    [Fact]
    public void PayeeNameIsEscaped_SoAnAmpersandCannotForgeAParameter()
    {
        // "A & B" unescaped would end the pn value and inject a bogus parameter into the query.
        var link = UpiPaymentLink.Build(Vpa, "A & B Waters", 10m, null);

        Assert.Contains("pn=A%20%26%20B%20Waters", link);
        Assert.DoesNotContain("&B", link);
    }

    [Fact]
    public void PayeeNameFallsBack_BecauseTheUpiSpecRequiresOne()
    {
        var link = UpiPaymentLink.Build(Vpa, null, 10m, null);
        Assert.Contains("pn=Merchant", link);
    }

    [Fact]
    public void NoteIsOmitted_WhenThereIsNoReference()
    {
        var link = UpiPaymentLink.Build(Vpa, "Shop", 10m, null);
        Assert.DoesNotContain("tn=", link);
    }

    [Fact]
    public void LongNoteIsTruncated_RatherThanRejectedByThePayerApp()
    {
        var link = UpiPaymentLink.Build(Vpa, "Shop", 10m, new string('X', 200));

        var note = link!.Split("tn=")[1];
        Assert.Equal(50, note.Length);
    }

    // ── the note that tells the owner WHOSE payment just landed ─────────────────

    [Fact]
    public void Reference_PutsTheIdentifierFirst_ThenTheName()
    {
        // Reference first because a payer app that truncates the display should still show the part
        // the owner can act on.
        Assert.Equal("CUST-00017 karan patel", UpiPaymentLink.Reference("CUST-00017", "karan patel"));
        Assert.Equal("INV-202608-0012 karan patel", UpiPaymentLink.Reference("INV-202608-0012", "karan patel"));
    }

    [Fact]
    public void Reference_CopesWithEitherHalfMissing()
    {
        Assert.Equal("karan patel", UpiPaymentLink.Reference(null, "karan patel"));
        Assert.Equal("CUST-00017", UpiPaymentLink.Reference("CUST-00017", null));
        Assert.Null(UpiPaymentLink.Reference(null, null));
        Assert.Null(UpiPaymentLink.Reference("  ", "  "));
    }

    [Fact]
    public void Reference_StripsCharactersAPayerAppMayReject()
    {
        // Customer names are free text the owner typed, so this sees commas, ampersands and emoji.
        // Anything outside the safe set is dropped rather than escaped: a rejected note can take the
        // whole intent down, not just lose the text.
        // "." survives on purpose — dropping it would mangle "Ltd." and initials.
        Assert.Equal("CUST-1 Sharma Patel Co.", UpiPaymentLink.Reference("CUST-1", "Sharma & Patel, Co."));
        Assert.Equal("CUST-2 Ravi", UpiPaymentLink.Reference("CUST-2", "Ravi 🙂"));
    }

    [Fact]
    public void Reference_CollapsesWhitespace_SoTheNoteStaysCompact()
        => Assert.Equal("CUST-3 Ravi Kumar", UpiPaymentLink.Reference("CUST-3", "  Ravi   Kumar  "));

    [Fact]
    public void Reference_TruncatesOnAWordBoundary_NotMidName()
    {
        var note = UpiPaymentLink.Reference("INV-202608-0012", "Ramesh Bhikhabhai Parshotambhai Kanzariya");

        Assert.True(note!.Length <= 50);
        Assert.DoesNotContain("  ", note);
        // Cut between words, so the note never ends on half a name.
        Assert.False(note.EndsWith(' '));
        Assert.StartsWith("INV-202608-0012 Ramesh", note);
    }

    [Fact]
    public void TheNoteReachesThePayload_Escaped()
    {
        var link = UpiPaymentLink.Build(Vpa, "Sharma RO", 25m,
            UpiPaymentLink.Reference("CUST-00017", "karan patel"));

        Assert.Contains("tn=CUST-00017%20karan%20patel", link);
    }
}
