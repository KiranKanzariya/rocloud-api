using MediatR;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Application.Features.Auth.Commands.GoogleLogin;

/// <summary>Sign in (or first-time link) with a Google ID token, scoped to a tenant.</summary>
public sealed record GoogleLoginCommand(string IdToken, string? TenantSubdomain) : IRequest<AuthResult>;
