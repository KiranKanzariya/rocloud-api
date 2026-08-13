using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Middleware;

namespace ROCloud.API.Controllers;

/// <summary>
/// Tells a native client whether its build is still supported (guide §mobile).
/// <para>
/// Anonymous and tenant-free on purpose: a blocked app has no session and no workspace, yet still
/// has to be able to read the store URL to get itself unstuck. It is also excluded from the version
/// gate itself — gating the endpoint that reports the gate would be a dead end.
/// </para>
/// </summary>
[ApiController]
[Route("api/app")]
public class AppVersionController : ControllerBase
{
    private readonly IConfiguration _config;

    public AppVersionController(IConfiguration config) => _config = config;

    /// <summary>
    /// Current update policy for the mobile app.
    /// <list type="bullet">
    /// <item><c>minSupportedBuild</c> — below this the app must block and force an update.</item>
    /// <item><c>latestBuild</c> — below this the app should offer a non-blocking update.</item>
    /// </list>
    /// Raise <c>minSupportedBuild</c> ONLY after the Play rollout has reached 100%. Raising it
    /// during a staged rollout walls off every user who has not received the update yet, with
    /// nothing available for them to install.
    /// </summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult Version()
    {
        var policy = AppVersionPolicy.From(_config);

        return Ok(new
        {
            minSupportedBuild = policy.MinSupportedBuild,
            latestBuild = policy.LatestBuild,
            storeUrl = policy.StoreUrl,
            message = policy.UpdateMessage
        });
    }
}
