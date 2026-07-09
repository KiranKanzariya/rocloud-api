using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ROCloud.Domain.Enums;

namespace ROCloud.Infrastructure.Persistence.Configurations;

/// <summary>
/// Custom enum↔string value converters for the two enums whose DB values are not
/// the C# member names: AuthProvider (lowercase) and BottleSize (digit-leading).
/// All other enums use EF's built-in string conversion (member name == DB value).
/// </summary>
public static class EnumConverters
{
    public static readonly ValueConverter<AuthProvider, string> AuthProviderConverter = new(
        v => v == AuthProvider.Google ? "google" : v == AuthProvider.Both ? "both" : "custom",
        v => v == "google" ? AuthProvider.Google : v == "both" ? AuthProvider.Both : AuthProvider.Custom);

    public static readonly ValueConverter<BottleSize, string> BottleSizeConverter = new(
        v => BottleSizeToDb(v),
        v => BottleSizeFromDb(v));

    private static string BottleSizeToDb(BottleSize v) => v switch
    {
        BottleSize.EighteenL => "18L",
        BottleSize.TwentyL => "20L",
        BottleSize.TwoFiftyMl => "250ml",
        BottleSize.FiveHundredMl => "500ml",
        BottleSize.OneL => "1L",
        _ => "Custom"
    };

    private static BottleSize BottleSizeFromDb(string v) => v switch
    {
        "18L" => BottleSize.EighteenL,
        "20L" => BottleSize.TwentyL,
        "250ml" => BottleSize.TwoFiftyMl,
        "500ml" => BottleSize.FiveHundredMl,
        "1L" => BottleSize.OneL,
        _ => BottleSize.Custom
    };
}
