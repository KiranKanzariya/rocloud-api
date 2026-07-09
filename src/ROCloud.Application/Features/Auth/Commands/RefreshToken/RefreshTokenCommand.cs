using MediatR;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Application.Features.Auth.Commands.RefreshToken;

/// <summary>Rotates the refresh token (read from the HttpOnly cookie by the controller).</summary>
public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResult>;
