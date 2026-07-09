namespace ROCloud.Domain.Enums;

/// <summary>
/// Authentication provider for a user. DB: users.auth_provider, which stores
/// lowercase values. A custom EF value converter (Phase 3) maps members to:
///   Custom -> "custom", Google -> "google", Both -> "both".
/// </summary>
public enum AuthProvider
{
    Custom,
    Google,
    Both
}
