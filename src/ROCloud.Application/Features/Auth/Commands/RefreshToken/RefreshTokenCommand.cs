using MediatR;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Rotates the refresh token (read from the HttpOnly cookie by the controller).
/// </summary>
/// <param name="Subdomain">
/// The workspace the request was made ON — the <c>X-Tenant</c> header or the host label. Optional,
/// and only ever used to REFUSE: a session may not be restored onto a workspace it does not belong
/// to. Without it, opening <c>pani.rocloud.in</c> while holding an Aqua session silently restored
/// Aqua — correct data, right permissions, and a URL saying otherwise.
/// </param>
public sealed record RefreshTokenCommand(string RefreshToken, string? Subdomain = null)
    : IRequest<AuthResult>;
