using MediatR;

namespace ROCloud.Application.Features.Auth.Commands.Logout;

/// <summary>Revokes the current access token (jti blocklist) and clears the refresh token.</summary>
public sealed record LogoutCommand : IRequest;
