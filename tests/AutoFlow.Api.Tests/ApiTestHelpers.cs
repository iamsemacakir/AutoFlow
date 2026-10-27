using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoFlow.Application.Customers;

namespace AutoFlow.Api.Tests;

/// <summary>Helpers shared by the endpoint tests.</summary>
internal static class ApiTestHelpers
{
    private const string VinAlphabet = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";

    public static Uri Url(string relative) => new(relative, UriKind.Relative);

    /// <summary>A random valid VIN, so tests sharing one database never collide.</summary>
    public static string UniqueVin() =>
        "WVW" + string.Concat(Enumerable.Range(0, 14).Select(_ => VinAlphabet[Random.Shared.Next(VinAlphabet.Length)]));

    public static async Task<CustomerDetails> CreateCustomerAsync(this HttpClient client, string firstName, string lastName)
    {
        using var response = await client.PostAsJsonAsync(
            Url("/api/customers"),
            new { firstName, lastName, email = $"{firstName}@example.com" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDetails>())!;
    }

    /// <summary>PostgreSQL stores microseconds, .NET timestamps have 100 ns ticks.</summary>
    public static void AssertSameInstant(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True((expected - actual).Duration() < TimeSpan.FromMilliseconds(1), $"{expected:O} != {actual:O}");

    /// <summary>The problem <c>code</c> of an error response, or <see langword="null"/> for a success.</summary>
    public static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return null;
        }

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }
}
