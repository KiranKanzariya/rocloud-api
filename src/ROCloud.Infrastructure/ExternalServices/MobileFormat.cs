namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>Formats a stored mobile number for MSG91, which expects country-code + number, digits only.</summary>
internal static class MobileFormat
{
    /// <summary>
    /// "+919876543210" / "919876543210" / "9876543210" → "919876543210". A bare 10-digit legacy number
    /// is assumed Indian and gets a 91 prefix; anything else is passed through as digits.
    /// </summary>
    public static string ToMsg91(string? mobile)
    {
        var digits = new string((mobile ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 10 ? "91" + digits : digits;
    }
}
