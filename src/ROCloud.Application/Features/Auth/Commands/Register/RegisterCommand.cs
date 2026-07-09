using MediatR;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Application.Features.Auth.Commands.Register;

/// <summary>Self-service tenant signup: creates the tenant (Trial), provisions roles, and the Owner user.</summary>
public sealed record RegisterCommand(
    string BusinessName,
    string OwnerName,
    string Email,
    string Password,
    string Mobile,
    string PlanType,
    string? Subdomain) : IRequest<AuthResult>;
