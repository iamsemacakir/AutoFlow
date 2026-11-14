using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;

namespace AutoFlow.Api.Tests;

/// <summary>Helpers shared by the endpoint tests.</summary>
internal static class ApiTestHelpers
{
    private const string VinAlphabet = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";

    public static Uri Url(string relative) => new(relative, UriKind.Relative);

    /// <summary>A random valid VIN, so tests sharing one database never collide.</summary>
    public static string UniqueVin() =>
        "WVW" + string.Concat(Enumerable.Range(0, 14).Select(_ => VinAlphabet[Random.Shared.Next(VinAlphabet.Length)]));

    /// <summary>A bay name no other test uses.</summary>
    public static string UniqueBayName() => $"Lift {Guid.NewGuid():N}";

    public static async Task<BayDetails> CreateBayAsync(this HttpClient client, string? name = null)
    {
        using var response = await client.PostAsJsonAsync(Url("/api/bays"), new { name = name ?? UniqueBayName() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BayDetails>())!;
    }

    public static async Task<CustomerDetails> CreateCustomerAsync(this HttpClient client, string firstName, string lastName)
    {
        using var response = await client.PostAsJsonAsync(
            Url("/api/customers"),
            new { firstName, lastName, email = $"{firstName}@example.com" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDetails>())!;
    }

    public static async Task<VehicleDetails> RegisterVehicleAsync(this HttpClient client, Guid customerId, int mileageKm = 30_000)
    {
        using var response = await client.PostAsJsonAsync(
            Url($"/api/customers/{customerId}/vehicles"),
            new { vin = UniqueVin(), licensePlate = "B-JB 42", make = "BMW", model = "320d", modelYear = 2021, mileageKm });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetails>())!;
    }

    public static async Task<WorkOrderDetails> OpenWorkOrderAsync(
        this HttpClient client, Guid vehicleId, string complaint = "Annual service")
    {
        using var response = await client.PostAsJsonAsync(Url($"/api/vehicles/{vehicleId}/work-orders"), new { complaint });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDetails>())!;
    }

    /// <summary>
    /// A moment on the 15-minute grid a week from today (UTC), so bookings
    /// made with the real clock are never in the past: <c>InAWeek(9)</c> is
    /// 09:00 UTC. Tests isolate themselves with fresh bays and vehicles.
    /// </summary>
    public static DateTimeOffset InAWeek(double hour) =>
        new DateTimeOffset(DateTime.UtcNow.Date.AddDays(7), TimeSpan.Zero).AddHours(hour);

    public static async Task<AppointmentDetails> BookAppointmentAsync(
        this HttpClient client, Guid vehicleId, Guid bayId, DateTimeOffset start, DateTimeOffset end)
    {
        using var response = await client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicleId}/appointments"),
            new { bayId, start, end, purpose = "Annual service" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AppointmentDetails>())!;
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
