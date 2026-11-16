using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoFlow.Application.Customers;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Concurrency;

/// <summary>
/// <c>ETag</c> and <c>If-Match</c> on single resources (ADR 0024): a client
/// that edits what it loaded is refused with 412 once someone else changed
/// the resource, instead of silently overwriting that change.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class ConditionalRequestTests : IDisposable
{
    private readonly HttpClient _client;

    public ConditionalRequestTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.CreateClient();
    }

    [Fact]
    public async Task Get_and_a_successful_change_answer_with_the_current_version_as_strong_etag()
    {
        var customer = await _client.CreateCustomerAsync("Lena", "Vogel");

        var loaded = await GetETagAsync($"/api/customers/{customer.Id}");
        using var update = await PutCustomerAsync(customer.Id, "Lena", ifMatch: null);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = update.Headers.ETag;
        Assert.NotNull(updated);
        Assert.False(loaded.IsWeak);
        Assert.False(updated.IsWeak);
        Assert.NotEqual(loaded, updated);
        Assert.Equal(updated, await GetETagAsync($"/api/customers/{customer.Id}"));
    }

    [Fact]
    public async Task A_change_with_the_current_etag_succeeds()
    {
        var customer = await _client.CreateCustomerAsync("Paul", "Krause");
        var etag = await GetETagAsync($"/api/customers/{customer.Id}");

        using var response = await PutCustomerAsync(customer.Id, "Pauline", etag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Pauline", (await response.Content.ReadFromJsonAsync<CustomerDetails>())!.FirstName);
    }

    [Fact]
    public async Task A_change_with_a_stale_etag_is_refused_with_412_and_changes_nothing()
    {
        var customer = await _client.CreateCustomerAsync("Jonas", "Becker");
        var stale = await GetETagAsync($"/api/customers/{customer.Id}");
        using (var first = await PutCustomerAsync(customer.Id, "Jonah", stale))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await PutCustomerAsync(customer.Id, "Johannes", stale);

        await AssertProblemAsync(second, HttpStatusCode.PreconditionFailed, "request.precondition_failed");
        var current = await _client.GetFromJsonAsync<CustomerDetails>(Url($"/api/customers/{customer.Id}"));
        Assert.Equal("Jonah", current!.FirstName);
    }

    [Fact]
    public async Task A_delete_with_a_stale_etag_is_refused_and_the_customer_stays()
    {
        var customer = await _client.CreateCustomerAsync("Emma", "Wolf");
        var stale = await GetETagAsync($"/api/customers/{customer.Id}");
        using (var change = await PutCustomerAsync(customer.Id, "Emmi", ifMatch: null))
        {
            change.EnsureSuccessStatusCode();
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, Url($"/api/customers/{customer.Id}"));
        request.Headers.IfMatch.Add(stale);
        using var response = await _client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed, "request.precondition_failed");
        using var stillThere = await _client.GetAsync(Url($"/api/customers/{customer.Id}"));
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task If_match_any_accepts_whatever_version_exists()
    {
        var customer = await _client.CreateCustomerAsync("Mila", "Hahn");

        using var response = await PutCustomerAsync(customer.Id, "Milena", EntityTagHeaderValue.Any);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_weak_etag_never_matches()
    {
        var customer = await _client.CreateCustomerAsync("Ben", "Schulz");
        var etag = await GetETagAsync($"/api/customers/{customer.Id}");

        using var response = await PutCustomerAsync(customer.Id, "Benjamin", new EntityTagHeaderValue(etag.Tag, isWeak: true));

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed, "request.precondition_failed");
    }

    [Fact]
    public async Task An_unknown_resource_is_still_404_with_if_match()
    {
        using var response = await PutCustomerAsync(Guid.NewGuid(), "Nobody", new EntityTagHeaderValue("\"1\""));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "customer.not_found");
    }

    [Fact]
    public async Task Adding_a_line_changes_the_work_orders_etag_so_an_estimate_cannot_be_approved_unseen()
    {
        var owner = await _client.CreateCustomerAsync("Clara", "Fischer");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        var workOrder = await _client.OpenWorkOrderAsync(vehicle.Id);
        var seenByAdvisor = await GetETagAsync($"/api/work-orders/{workOrder.Id}");

        using (var line = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/labour-lines"),
            new { description = "Replace front brake discs", hours = 1.5m, hourlyRate = 95m }))
        {
            line.EnsureSuccessStatusCode();
            Assert.NotEqual(seenByAdvisor, line.Headers.ETag);
        }

        using var approve = new HttpRequestMessage(HttpMethod.Post, Url($"/api/work-orders/{workOrder.Id}/approve"));
        approve.Headers.IfMatch.Add(seenByAdvisor);
        using var response = await _client.SendAsync(approve);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed, "request.precondition_failed");
    }

    public void Dispose() => _client.Dispose();

    private async Task<EntityTagHeaderValue> GetETagAsync(string path)
    {
        using var response = await _client.GetAsync(Url(path));
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag ?? throw new InvalidOperationException($"GET {path} sent no ETag.");
    }

    private async Task<HttpResponseMessage> PutCustomerAsync(Guid id, string firstName, EntityTagHeaderValue? ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Url($"/api/customers/{id}"))
        {
            Content = JsonContent.Create(new { firstName, lastName = "Test", email = $"{firstName}@example.com" }),
        };
        if (ifMatch is not null)
        {
            request.Headers.IfMatch.Add(ifMatch);
        }

        return await _client.SendAsync(request);
    }
}
