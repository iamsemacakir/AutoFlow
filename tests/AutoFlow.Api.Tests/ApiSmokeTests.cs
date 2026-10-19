using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoFlow.Api.Tests;

public class ApiSmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Root_endpoint_returns_api_info()
    {
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("AutoFlow API", body, StringComparison.Ordinal);
    }
}
