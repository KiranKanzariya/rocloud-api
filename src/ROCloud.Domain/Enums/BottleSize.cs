namespace ROCloud.Domain.Enums;

/// <summary>
/// Bottle / jar size. DB: products.bottle_size, customers.preferred_bottle_size.
/// The DB stores digit-leading strings that are not valid C# identifiers, so a
/// custom EF value converter (Phase 3) maps members to these wire values:
///   EighteenL -> "18L", TwentyL -> "20L", TwoFiftyMl -> "250ml",
///   FiveHundredMl -> "500ml", OneL -> "1L", Custom -> "Custom".
/// </summary>
public enum BottleSize
{
    EighteenL,
    TwentyL,
    TwoFiftyMl,
    FiveHundredMl,
    OneL,
    Custom
}
