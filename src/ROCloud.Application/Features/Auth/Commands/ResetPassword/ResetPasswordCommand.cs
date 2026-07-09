using MediatR;

namespace ROCloud.Application.Features.Auth.Commands.ResetPassword;

/// <summary>Completes password recovery using the token issued by ForgotPassword.</summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest;
