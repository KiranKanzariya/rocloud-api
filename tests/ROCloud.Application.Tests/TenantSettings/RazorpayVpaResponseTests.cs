using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Infrastructure.ExternalServices;

namespace ROCloud.Application.Tests.TenantSettings;

/// <summary>
/// How Razorpay's replies are read.
///
/// <para>The case that matters is a 400. Razorpay uses it both for "that id is not valid" and for
/// "this endpoint is not enabled on your account" — the second arriving as
/// <c>"The requested URL was not found on the server."</c>, which is what a live ROCloud key actually
/// returns today. Reading that as a verdict would tell every owner their working UPI id is invalid and
/// talk them out of a setup that is fine. "Could not check" is the only honest answer.</para>
/// </summary>
public class RazorpayVpaResponseTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private static RazorpayService Service(HttpStatusCode status, string body)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Razorpay:KeyId"] = "rzp_test_key",
            ["Razorpay:KeySecret"] = "secret",
        }).Build();

        return new RazorpayService(
            new HttpClient(new StubHandler(status, body)), config, NullLogger<RazorpayService>.Instance);
    }

    private const string EndpointNotEnabled =
        """{"error":{"code":"BAD_REQUEST_ERROR","description":"The requested URL was not found on the server.","source":"internal","step":"NA","reason":"NA","metadata":{}}}""";

    [Fact]
    public async Task AnEndpointWeCannotReach_IsUnavailable_NotAnInvalidId()
    {
        var result = await Service(HttpStatusCode.BadRequest, EndpointNotEnabled)
            .ValidateVpaAsync("dabhiro@okaxis");

        Assert.True(result.Unavailable);
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task A400ThatNamesTheAddress_IsAVerdictOnTheId()
    {
        var result = await Service(HttpStatusCode.BadRequest,
            """{"error":{"code":"BAD_REQUEST_ERROR","description":"The payment address is invalid"}}""")
            .ValidateVpaAsync("nosuchid@okaxis");

        Assert.False(result.Valid);
        Assert.False(result.Unavailable);   // Razorpay looked, and answered
    }

    [Fact]
    public async Task AGoodId_ReturnsTheNameItIsRegisteredTo()
    {
        var result = await Service(HttpStatusCode.OK,
            """{"vpa":"dabhiro@okaxis","success":true,"customer_name":"Kiran Kanzariya"}""")
            .ValidateVpaAsync("dabhiro@okaxis");

        Assert.True(result.Valid);
        Assert.Equal("Kiran Kanzariya", result.PayeeName);
    }

    [Fact]
    public async Task A200SayingTheIdDoesNotExist_IsAVerdict_NotAnOutage()
    {
        var result = await Service(HttpStatusCode.OK, """{"vpa":"nope@okaxis","success":false}""")
            .ValidateVpaAsync("nope@okaxis");

        Assert.False(result.Valid);
        Assert.False(result.Unavailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OurAccessOrTheirService_IsNeverBlamedOnTheOwnersId(HttpStatusCode status)
    {
        var result = await Service(status, """{"error":{"code":"BAD_REQUEST_ERROR"}}""").ValidateVpaAsync("x@okaxis");

        Assert.True(result.Unavailable);
    }
}
