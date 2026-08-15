namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// What the current request says about the device it came from.
/// </summary>
/// <remarks>
/// Ambient rather than a parameter on every command. Five separate paths issue a session — password
/// login, Google login, both registrations, and the handoff — and the label is not an input to any of
/// their decisions; it is request metadata, like the tenant or the locale. Threading it through five
/// records and five handlers would put a cosmetic field into five signatures and still leave the sixth
/// caller to forget it.
/// </remarks>
public interface IDeviceContext
{
    /// <summary>Human-readable device name, or null when the request says nothing useful.</summary>
    string? Label { get; }
}
