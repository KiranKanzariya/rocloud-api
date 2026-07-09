namespace ROCloud.Application.Features.Auth.Commands.GoogleWorkspaces;

/// <summary>
/// A workspace a Google identity can sign in to, plus a ready-to-use one-time handoff URL that
/// establishes the session on that tenant's subdomain.
/// </summary>
public sealed record GoogleWorkspaceDto(string Subdomain, string TenantName, string HandoffUrl);
