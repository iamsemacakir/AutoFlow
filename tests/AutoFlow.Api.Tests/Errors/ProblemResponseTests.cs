using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Errors;

/// <summary>
/// Requests that are rejected before any use case runs: invalid or unreadable
/// bodies, unmatched routes and methods. None of them reaches the database, so
/// the host runs without a PostgreSQL container.
/// </summary>
public sealed class ProblemResponseTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_customer_with_missing_fields_lists_every_field_error()
    {
        using var response = await _client.PostAsJsonAsync(Url("/api/customers"), new { phoneNumber = "+49 89 1" });

        var errors = await AssertValidationProblemAsync(response);
        Assert.Equal("email, firstName, lastName", FieldNames(errors));
        Assert.Equal("'firstName' is required.", Assert.Single(errors["firstName"]));
    }

    [Fact]
    public async Task Put_customer_with_overlong_values_is_rejected_with_400()
    {
        using var response = await _client.PutAsJsonAsync(
            Url($"/api/customers/{Guid.NewGuid()}"),
            new { firstName = new string('a', 101), lastName = "Schmidt", email = "anna@example.com", phoneNumber = new string('1', 33) });

        var errors = await AssertValidationProblemAsync(response);
        Assert.Equal("firstName, phoneNumber", FieldNames(errors));
        Assert.Equal("'firstName' must be at most 100 characters.", Assert.Single(errors["firstName"]));
    }

    [Fact]
    public async Task Post_vehicle_with_missing_and_out_of_range_fields_is_rejected_with_400()
    {
        using var response = await _client.PostAsJsonAsync(
            Url($"/api/customers/{Guid.NewGuid()}/vehicles"),
            new { vin = "WVWZZZ1JZXW000001", licensePlate = " ", make = "VW", modelYear = 1800, mileageKm = -1 });

        var errors = await AssertValidationProblemAsync(response);
        Assert.Equal("licensePlate, mileageKm, model, modelYear", FieldNames(errors));
        Assert.Equal("'modelYear' must be 1886 or greater.", Assert.Single(errors["modelYear"]));
    }

    [Fact]
    public async Task Put_mileage_without_a_reading_is_rejected_with_400_instead_of_recording_zero()
    {
        using var response = await _client.PutAsJsonAsync(Url($"/api/vehicles/{Guid.NewGuid()}/mileage"), new { });

        var errors = await AssertValidationProblemAsync(response);
        Assert.Equal("'mileageKm' is required.", Assert.Single(errors["mileageKm"]));
    }

    [Fact]
    public async Task Post_transfer_to_the_empty_id_is_rejected_with_400()
    {
        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{Guid.NewGuid()}/transfer"),
            new { customerId = Guid.Empty });

        var errors = await AssertValidationProblemAsync(response);
        Assert.Single(errors["customerId"]);
    }

    [Theory]
    [InlineData("{\"firstName\": \"Anna\",")]
    [InlineData("{\"firstName\": 42, \"lastName\": \"Schmidt\", \"email\": \"anna@example.com\"}")]
    public async Task Post_customer_with_unreadable_json_returns_400_malformed(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(Url("/api/customers"), content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.malformed");
    }

    [Fact]
    public async Task Post_mileage_with_a_number_of_the_wrong_type_returns_400_malformed()
    {
        using var content = new StringContent("{\"mileageKm\": \"lots\"}", Encoding.UTF8, "application/json");
        using var response = await _client.PutAsync(Url($"/api/vehicles/{Guid.NewGuid()}/mileage"), content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.malformed");
    }

    [Fact]
    public async Task Get_list_with_an_unparsable_page_returns_400_malformed()
    {
        using var response = await _client.GetAsync(Url("/api/customers?page=abc"));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.malformed");
    }

    [Fact]
    public async Task Post_customer_as_plain_text_returns_415()
    {
        using var content = new StringContent("Anna Schmidt", Encoding.UTF8, "text/plain");
        using var response = await _client.PostAsync(Url("/api/customers"), content);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "request.unsupported_media_type");
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/api/customers/not-a-guid")]
    public async Task Unmatched_route_returns_404_problem(string path)
    {
        using var response = await _client.GetAsync(Url(path));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "route.not_found");
    }

    [Fact]
    public async Task Unsupported_method_returns_405_problem()
    {
        using var response = await _client.DeleteAsync(Url($"/api/vehicles/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "request.method_not_allowed");
    }

    [Fact]
    public async Task Every_problem_names_the_request_path_and_a_trace_id()
    {
        var path = $"/api/vehicles/{Guid.NewGuid()}/mileage";
        using var response = await _client.PutAsJsonAsync(Url(path), new { mileageKm = -5 });

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(path, problem.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
    }

    public void Dispose() => _client.Dispose();

    private static string FieldNames(Dictionary<string, string[]> errors) =>
        string.Join(", ", errors.Keys.Order(StringComparer.Ordinal));

    private static async Task<Dictionary<string, string[]>> AssertValidationProblemAsync(HttpResponseMessage response)
    {
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }
}
