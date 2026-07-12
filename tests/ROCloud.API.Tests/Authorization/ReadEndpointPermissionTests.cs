using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;

namespace ROCloud.API.Tests.Authorization;

/// <summary>
/// A GET must never be gated behind a write permission. Reading a resource and administering it are
/// different rights; conflating them forces a page to fire requests its user cannot make, which
/// surfaces as a spurious "no permission" toast and, for custom roles, a dead-end UI (e.g. an empty
/// role dropdown with no way to assign one).
/// </summary>
public class ReadEndpointPermissionTests
{
    private static bool IsRead(string permission) =>
        permission.EndsWith(".View", StringComparison.Ordinal)
        || permission.EndsWith(".ViewOwn", StringComparison.Ordinal);

    /// <summary>Every GET action, paired with the attributes gating it (its own and its controller's).</summary>
    private static IEnumerable<(MethodInfo Action, T Attribute)> GatedGets<T>() where T : Attribute =>
        typeof(RequirePermissionAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpGetAttribute>().Any())
            .SelectMany(m => m.GetCustomAttributes<T>()
                .Concat(m.DeclaringType!.GetCustomAttributes<T>())
                .Select(a => (m, a)));

    /// <summary>
    /// Guards the two tests below from passing vacuously: if reflection stopped finding actions
    /// (renamed attribute, moved controllers), they would report zero offenders and go green.
    /// </summary>
    [Fact]
    public void Reflection_finds_the_permission_gated_get_endpoints()
    {
        Assert.True(GatedGets<RequirePermissionAttribute>().Count() >= 30);
        Assert.True(GatedGets<RequireAnyPermissionAttribute>().Any());
    }

    [Fact]
    public void Get_endpoints_never_require_a_write_permission()
    {
        var offenders = GatedGets<RequirePermissionAttribute>()
            .Where(x => !IsRead(x.Attribute.Permission))
            .Select(x => $"{x.Action.DeclaringType!.Name}.{x.Action.Name} requires {x.Attribute.Permission}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "GET endpoints gated behind a write permission:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Get_endpoints_with_alternatives_offer_at_least_one_read_permission()
    {
        var offenders = GatedGets<RequireAnyPermissionAttribute>()
            .Where(x => !x.Attribute.Permissions.Any(IsRead))
            .Select(x => $"{x.Action.DeclaringType!.Name}.{x.Action.Name} requires any of "
                         + string.Join(", ", x.Attribute.Permissions))
            .ToList();

        Assert.True(offenders.Count == 0,
            "GET endpoints reachable only via write permissions:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }
}
