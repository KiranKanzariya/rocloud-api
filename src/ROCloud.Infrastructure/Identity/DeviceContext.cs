using Microsoft.AspNetCore.Http;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Infrastructure.Identity;

/// <summary>Reads the device label from the current request's headers. See <see cref="IDeviceContext"/>.</summary>
public class DeviceContext : IDeviceContext
{
    private readonly IHttpContextAccessor _http;

    public DeviceContext(IHttpContextAccessor http) => _http = http;

    public string? Label
    {
        get
        {
            var request = _http.HttpContext?.Request;
            if (request is null) return null;

            return DeviceLabel.From(
                request.Headers[DeviceLabel.Header].FirstOrDefault(),
                request.Headers.UserAgent.FirstOrDefault());
        }
    }
}
