using MediatR;

namespace ROCloud.Application.Features.Auth.Commands.ForgotPassword;

/// <summary>Starts password recovery. Always succeeds (never reveals whether the email exists).</summary>
public sealed record ForgotPasswordCommand(string Email, string? TenantSubdomain) : IRequest;
