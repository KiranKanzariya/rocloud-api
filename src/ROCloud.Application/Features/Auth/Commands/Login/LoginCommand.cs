using MediatR;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Application.Features.Auth.Commands.Login;

/// <summary>Custom email/password login. TenantSubdomain is resolved by the controller from X-Tenant/host.</summary>
public sealed record LoginCommand(string Email, string Password, string? TenantSubdomain) : IRequest<AuthResult>;
