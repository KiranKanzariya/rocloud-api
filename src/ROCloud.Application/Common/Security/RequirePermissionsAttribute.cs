namespace ROCloud.Application.Common.Security;

/// <summary>
/// Declares the permission code(s) a MediatR request requires. Enforced by
/// AuthorizationBehaviour. (Controller endpoints use the [RequirePermission] filter
/// from Phase 6; this is the request-level equivalent.)
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequirePermissionsAttribute : Attribute
{
    public RequirePermissionsAttribute(params string[] permissions) => Permissions = permissions;

    public string[] Permissions { get; }
}
