using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace ROCloud.API.Logging;

/// <summary>
/// Masks mobile numbers and email addresses in log message properties (guide §10.13)
/// so PII never lands in logs in plain text.
/// </summary>
public partial class SensitiveDataEnricher : ILogEventEnricher
{
    [GeneratedRegex(@"\+?\d{10,15}")]
    private static partial Regex MobileRegex();

    [GeneratedRegex(@"[\w\.-]+@[\w\.-]+\.\w+")]
    private static partial Regex EmailRegex();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var prop in logEvent.Properties.ToList())
        {
            if (prop.Value is ScalarValue { Value: string s })
            {
                var masked = Mask(s);
                if (!string.Equals(masked, s, StringComparison.Ordinal))
                    logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(prop.Key, masked));
            }
        }
    }

    /// <summary>Masks mobile numbers and emails in a string (guide §10.13).</summary>
    public static string Mask(string input) => MaskEmails(MaskMobiles(input));

    private static string MaskMobiles(string input) => MobileRegex().Replace(input, m =>
    {
        var v = m.Value;
        return v.Length <= 5 ? new string('*', v.Length) : $"{v[..3]}******{v[^2..]}";
    });

    private static string MaskEmails(string input) => EmailRegex().Replace(input, m =>
    {
        var parts = m.Value.Split('@');
        var local = parts[0];
        var prefix = local.Length >= 2 ? local[..2] : local;
        return $"{prefix}***@{parts[1]}";
    });
}
