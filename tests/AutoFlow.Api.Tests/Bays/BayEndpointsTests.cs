using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Bays;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Bays;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class BayEndpointsTests : IDisposable
{
    private readonly HttpClient _client;

    public BayEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Create_answers_201_with_the_location_of_the_new_bay_in_service()
    {
        var name = UniqueBayName();

        using var response = await _client.PostAsJsonAsync(Url("/api/bays"), new { name = $"  {name} " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BayDetails>();
        Assert.Equal(name, created!.Name);
        Assert.True(created.IsInService);
        Assert.EndsWith($"/api/bays/{created.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var loaded = await _client.GetFromJsonAsync<BayDetails>(response.Headers.Location);
        Assert.Equal(created.Id, loaded!.Id);
    }

    [Fact]
    public async Task Create_with_a_name_in_use_returns_409()
    {
        var existing = await _client.CreateBayAsync();

        using var response = await _client.PostAsJsonAsync(Url("/api/bays"), new { name = existing.Name });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "bay.duplicate_name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Create_without_a_name_returns_400(string? name)
    {
        using var response = await _client.PostAsJsonAsync(Url("/api/bays"), new { name });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Create_with_an_overlong_name_returns_400()
    {
        using var response = await _client.PostAsJsonAsync(Url("/api/bays"), new { name = new string('x', 61) });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task List_returns_bays_in_and_out_of_service_ordered_by_name()
    {
        var prefix = $"Row {Guid.NewGuid():N}";
        var second = await _client.CreateBayAsync($"{prefix} B");
        var first = await _client.CreateBayAsync($"{prefix} A");
        (await PostActionAsync(second.Id, "take-out-of-service")).Dispose();

        var bays = await _client.GetFromJsonAsync<List<BayDetails>>(Url("/api/bays"));

        var ours = bays!.Where(b => b.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        Assert.Equal([first.Id, second.Id], ours.Select(b => b.Id).ToArray());
        Assert.Equal([true, false], ours.Select(b => b.IsInService).ToArray());
    }

    [Fact]
    public async Task Get_of_an_unknown_bay_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/bays/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "bay.not_found");
    }

    [Fact]
    public async Task Rename_changes_the_name_and_keeping_the_name_is_allowed()
    {
        var bay = await _client.CreateBayAsync();
        var newName = UniqueBayName();

        using var renamed = await _client.PutAsJsonAsync(Url($"/api/bays/{bay.Id}"), new { name = newName });
        using var unchanged = await _client.PutAsJsonAsync(Url($"/api/bays/{bay.Id}"), new { name = newName });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        var loaded = await _client.GetFromJsonAsync<BayDetails>(Url($"/api/bays/{bay.Id}"));
        Assert.Equal(newName, loaded!.Name);
    }

    [Fact]
    public async Task Rename_to_the_name_of_another_bay_returns_409_and_changes_nothing()
    {
        var bay = await _client.CreateBayAsync();
        var other = await _client.CreateBayAsync();

        using var response = await _client.PutAsJsonAsync(Url($"/api/bays/{bay.Id}"), new { name = other.Name });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "bay.duplicate_name");
        var loaded = await _client.GetFromJsonAsync<BayDetails>(Url($"/api/bays/{bay.Id}"));
        Assert.Equal(bay.Name, loaded!.Name);
    }

    [Fact]
    public async Task Rename_of_an_unknown_bay_returns_404()
    {
        using var response = await _client.PutAsJsonAsync(
            Url($"/api/bays/{Guid.NewGuid()}"),
            new { name = UniqueBayName() });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "bay.not_found");
    }

    [Fact]
    public async Task Take_out_of_service_and_return_to_service_toggle_the_bay()
    {
        var bay = await _client.CreateBayAsync();

        using var closed = await PostActionAsync(bay.Id, "take-out-of-service");
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.False((await closed.Content.ReadFromJsonAsync<BayDetails>())!.IsInService);

        using var reopened = await PostActionAsync(bay.Id, "return-to-service");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.True((await reopened.Content.ReadFromJsonAsync<BayDetails>())!.IsInService);
    }

    [Fact]
    public async Task Return_to_service_of_a_bay_in_service_returns_422()
    {
        var bay = await _client.CreateBayAsync();

        using var response = await PostActionAsync(bay.Id, "return-to-service");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task Taking_a_bay_out_of_service_twice_returns_422()
    {
        var bay = await _client.CreateBayAsync();
        (await PostActionAsync(bay.Id, "take-out-of-service")).Dispose();

        using var response = await PostActionAsync(bay.Id, "take-out-of-service");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Theory]
    [InlineData("take-out-of-service")]
    [InlineData("return-to-service")]
    public async Task Service_action_on_an_unknown_bay_returns_404(string action)
    {
        using var response = await PostActionAsync(Guid.NewGuid(), action);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "bay.not_found");
    }

    [Fact]
    public async Task Bays_cannot_be_deleted()
    {
        var bay = await _client.CreateBayAsync();

        using var response = await _client.DeleteAsync(Url($"/api/bays/{bay.Id}"));

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "request.method_not_allowed");
    }

    public void Dispose() => _client.Dispose();

    private Task<HttpResponseMessage> PostActionAsync(Guid bayId, string action) =>
        _client.PostAsync(Url($"/api/bays/{bayId}/{action}"), content: null);
}
