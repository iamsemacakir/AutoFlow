using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.Api.Tests.Customers;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class CustomerEndpointsTests : IDisposable
{
    private readonly ApiDatabaseFixture _api;
    private readonly HttpClient _client;

    public CustomerEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Post_creates_a_customer_that_can_be_read_from_its_location()
    {
        using var response = await _client.PostAsJsonAsync(
            Url("/api/customers"),
            new { firstName = " Anna ", lastName = "Schmidt", email = "Anna@Example.com", phoneNumber = "+49 89 123456" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CustomerDetails>();
        Assert.NotNull(created);
        Assert.Equal("Anna", created.FirstName);
        Assert.Equal("anna@example.com", created.Email);
        Assert.EndsWith($"/api/customers/{created.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var loaded = await _client.GetFromJsonAsync<CustomerDetails>(response.Headers.Location);

        Assert.NotNull(loaded);
        Assert.Equal(created with { CreatedAt = loaded.CreatedAt }, loaded);
        AssertSameInstant(created.CreatedAt, loaded.CreatedAt);
    }

    [Fact]
    public async Task Post_with_an_invalid_email_is_rejected_with_422()
    {
        using var response = await _client.PostAsJsonAsync(
            Url("/api/customers"),
            new { firstName = "Anna", lastName = "Schmidt", email = "not-an-email" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_unknown_customer_returns_404_problem_with_code()
    {
        using var response = await _client.GetAsync(Url($"/api/customers/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "customer.not_found");
    }

    [Fact]
    public async Task Put_replaces_name_and_contact_details()
    {
        var customer = await CreateCustomerAsync("Anna", "Schmidt");

        using var response = await _client.PutAsJsonAsync(
            Url($"/api/customers/{customer.Id}"),
            new { firstName = "Anna", lastName = "Müller", email = "anna.mueller@example.com", phoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loaded = await _client.GetFromJsonAsync<CustomerDetails>(Url($"/api/customers/{customer.Id}"));
        Assert.NotNull(loaded);
        Assert.Equal("Müller", loaded.LastName);
        Assert.Equal("anna.mueller@example.com", loaded.Email);
        Assert.Null(loaded.PhoneNumber);
        AssertSameInstant(customer.CreatedAt, loaded.CreatedAt);
    }

    [Fact]
    public async Task Put_unknown_customer_returns_404()
    {
        using var response = await _client.PutAsJsonAsync(
            Url($"/api/customers/{Guid.NewGuid()}"),
            new { firstName = "Anna", lastName = "Schmidt", email = "anna@example.com" });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "customer.not_found");
    }

    [Fact]
    public async Task Delete_removes_a_customer_without_vehicles()
    {
        var customer = await CreateCustomerAsync("Anna", "Schmidt");

        using var deleted = await _client.DeleteAsync(Url($"/api/customers/{customer.Id}"));
        using var lookup = await _client.GetAsync(Url($"/api/customers/{customer.Id}"));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task Delete_customer_who_owns_a_vehicle_returns_409()
    {
        var customer = await CreateCustomerAsync("Jonas", "Becker");
        using (var scope = _api.Factory.Services.CreateScope())
        {
            var registered = await scope.ServiceProvider.GetRequiredService<RegisterVehicleHandler>().HandleAsync(
                new RegisterVehicleCommand(customer.Id, UniqueVin(), "B-JB 42", "BMW", "320d", 2021, 30_000));
            Assert.True(registered.IsSuccess);
        }

        using var response = await _client.DeleteAsync(Url($"/api/customers/{customer.Id}"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "customer.has_vehicles");
    }

    [Fact]
    public async Task Get_list_searches_and_pages()
    {
        var token = "t" + Guid.NewGuid().ToString("N")[..12];
        var becker = await CreateCustomerAsync("Jonas", token + "Becker");
        var schmidt = await CreateCustomerAsync("Anna", token + "Schmidt");
        await CreateCustomerAsync("Mia", token + "Wagner");

        var first = await _client.GetFromJsonAsync<PagedList<CustomerDetails>>(
            Url($"/api/customers?search={token}&page=1&pageSize=2"));
        var narrowed = await _client.GetFromJsonAsync<PagedList<CustomerDetails>>(
            Url($"/api/customers?search={token}%20anna"));

        Assert.NotNull(first);
        Assert.Equal(new[] { becker.Id, schmidt.Id }, first.Items.Select(c => c.Id));
        Assert.Equal((1, 2, 3, 2), (first.Page, first.PageSize, first.TotalCount, first.TotalPages));
        Assert.NotNull(narrowed);
        Assert.Equal(new[] { schmidt.Id }, narrowed.Items.Select(c => c.Id));
        Assert.Equal(Paging.DefaultPageSize, narrowed.PageSize);
    }

    [Theory]
    [InlineData("pageSize=101", "paging.invalid_page_size")]
    [InlineData("page=0", "paging.invalid_page")]
    public async Task Get_list_with_paging_out_of_range_returns_400(string queryString, string code)
    {
        using var response = await _client.GetAsync(Url($"/api/customers?{queryString}"));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    public void Dispose() => _client.Dispose();

    private static Uri Url(string relative) => new(relative, UriKind.Relative);

    private static string UniqueVin()
    {
        const string alphabet = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
        return "WVW" + string.Concat(Enumerable.Range(0, 14).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]));
    }

    private async Task<CustomerDetails> CreateCustomerAsync(string firstName, string lastName)
    {
        using var response = await _client.PostAsJsonAsync(
            Url("/api/customers"),
            new { firstName, lastName, email = $"{firstName}@example.com" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDetails>())!;
    }

    /// <summary>PostgreSQL stores microseconds, .NET timestamps have 100 ns ticks.</summary>
    private static void AssertSameInstant(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True((expected - actual).Duration() < TimeSpan.FromMilliseconds(1), $"{expected:O} != {actual:O}");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }
}
