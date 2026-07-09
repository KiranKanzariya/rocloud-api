namespace ROCloud.Domain.Enums;

/// <summary>
/// Maps <see cref="BottleSize"/> to/from its wire string (e.g. TwentyL ⇄ "20L"),
/// since the members are not valid as the digit-leading wire values.
/// </summary>
public static class BottleSizeExtensions
{
    public static string ToWire(this BottleSize size) => size switch
    {
        BottleSize.EighteenL => "18L",
        BottleSize.TwentyL => "20L",
        BottleSize.TwoFiftyMl => "250ml",
        BottleSize.FiveHundredMl => "500ml",
        BottleSize.OneL => "1L",
        _ => "Custom"
    };

    public static BottleSize? FromWire(string? wire) => wire switch
    {
        "18L" => BottleSize.EighteenL,
        "20L" => BottleSize.TwentyL,
        "250ml" => BottleSize.TwoFiftyMl,
        "500ml" => BottleSize.FiveHundredMl,
        "1L" => BottleSize.OneL,
        "Custom" => BottleSize.Custom,
        _ => null
    };
}
