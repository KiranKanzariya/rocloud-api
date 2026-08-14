using MediatR;

namespace ROCloud.Application.Features.Auth.Commands.Logout;

/// <summary>
/// Revokes the current access token (jti blocklist) and ends the session the refresh token belongs
/// to. <paramref name="RefreshToken"/> is optional: an older client that sends none still gets its
/// access token blocklisted, and its session row simply expires on its own.
/// </summary>
public sealed record LogoutCommand(string? RefreshToken = null) : IRequest;
