using MediatR;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Application.Features.Auth.Commands.RegisterGoogle;

/// <summary>
/// Self-service tenant signup using a Google account (no password). The owner's name and email come
/// from the verified Google id-token; the business/plan/subdomain come from the form.
/// </summary>
public sealed record RegisterGoogleCommand(
    string IdToken,
    string BusinessName,
    string? Mobile,
    string PlanType,
    string? Subdomain) : IRequest<AuthResult>;
