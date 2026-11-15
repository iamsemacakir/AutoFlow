using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoFlow.Application.Users;
using AutoFlow.Domain.Users;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Auth;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class UserEndpointsTests : IDisposable
{
    private readonly ApiDatabaseFixture _api;
    private readonly HttpClient _owner;

    public UserEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _owner = api.CreateClient(UserRole.Owner);
    }

    [Fact]
    public async Task Owner_opens_an_account_that_can_be_read_and_listed()
    {
        var email = UniqueEmail();

        using var response = await CreateAsync(email, "ServiceAdvisor");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserDetails>();
        Assert.Equal((email, "New Colleague", "ServiceAdvisor", true), (created!.Email, created.DisplayName, created.Role, created.IsActive));
        Assert.Null(created.LastSignInAt);
        Assert.EndsWith($"/api/users/{created.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var loaded = await _owner.GetFromJsonAsync<UserDetails>(response.Headers.Location);
        var all = await _owner.GetFromJsonAsync<List<UserDetails>>(Url("/api/users"));
        Assert.Equal(created.Id, loaded!.Id);
        Assert.Contains(all!, user => user.Id == created.Id);
        Assert.Contains(all!, user => user.Email == ApiDatabaseFixture.EmailFor(UserRole.Owner) && user.Role == "Owner");
    }

    [Fact]
    public async Task Account_with_an_email_in_use_returns_409()
    {
        using var response = await CreateAsync(ApiDatabaseFixture.EmailFor(UserRole.Mechanic).ToUpperInvariant(), "Mechanic");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "user.duplicate_email");
    }

    [Fact]
    public async Task Account_with_an_unknown_role_or_a_short_password_is_refused()
    {
        using var unknownRole = await CreateAsync(UniqueEmail(), "Apprentice");
        using var shortPassword = await CreateAsync(UniqueEmail(), "Mechanic", password: "short");
        using var missing = await _owner.PostAsJsonAsync(Url("/api/users"), new { });

        await AssertProblemAsync(unknownRole, HttpStatusCode.BadRequest, "user.unknown_role");
        await AssertProblemAsync(shortPassword, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        await AssertProblemAsync(missing, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Theory]
    [InlineData(UserRole.ServiceAdvisor)]
    [InlineData(UserRole.Mechanic)]
    public async Task Only_owners_manage_accounts(UserRole role)
    {
        using var client = _api.CreateClient(role);

        using var list = await client.GetAsync(Url("/api/users"));
        using var create = await client.PostAsJsonAsync(
            Url("/api/users"),
            new { email = UniqueEmail(), displayName = "Sneaky", role = "Owner", password = ApiDatabaseFixture.StaffPassword });

        await AssertProblemAsync(list, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertProblemAsync(create, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task Accounts_are_closed_to_anonymous_requests()
    {
        using var anonymous = _api.Factory.CreateClient();

        using var response = await anonymous.GetAsync(Url("/api/users"));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task Deactivation_ends_sign_in_and_existing_tokens_at_once()
    {
        var email = UniqueEmail();
        var user = await CreateUserAsync(email, "Mechanic");
        using var anonymous = _api.Factory.CreateClient();
        var token = await ApiDatabaseFixture.SignInAsync(anonymous, email);

        using var deactivated = await _owner.PostAsync(Url($"/api/users/{user.Id}/deactivate"), content: null);
        using var withOldToken = await GetMeAsync(token);
        using var signIn = await anonymous.PostAsJsonAsync(
            Url("/api/auth/token"),
            new { email, password = ApiDatabaseFixture.StaffPassword });

        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await deactivated.Content.ReadFromJsonAsync<UserDetails>())!.IsActive);
        await AssertProblemAsync(withOldToken, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await AssertProblemAsync(signIn, HttpStatusCode.Unauthorized, "auth.invalid_credentials");

        using var reactivated = await _owner.PostAsync(Url($"/api/users/{user.Id}/reactivate"), content: null);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.NotEmpty(await ApiDatabaseFixture.SignInAsync(anonymous, email));
    }

    [Fact]
    public async Task Role_change_takes_effect_on_the_next_request_with_the_same_token()
    {
        var email = UniqueEmail();
        var user = await CreateUserAsync(email, "Mechanic");
        using var client = _api.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiDatabaseFixture.SignInAsync(client, email));

        using var before = await client.GetAsync(Url("/api/users"));
        using var promoted = await _owner.PutAsJsonAsync(Url($"/api/users/{user.Id}/role"), new { role = "Owner" });
        using var after = await client.GetAsync(Url("/api/users"));
        using var demoted = await _owner.PutAsJsonAsync(Url($"/api/users/{user.Id}/role"), new { role = "Mechanic" });

        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        Assert.Equal("Owner", (await promoted.Content.ReadFromJsonAsync<UserDetails>())!.Role);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);
    }

    [Fact]
    public async Task Owner_cannot_deactivate_themselves()
    {
        var me = await _owner.GetFromJsonAsync<UserDetails>(Url("/api/auth/me"));

        using var response = await _owner.PostAsync(Url($"/api/users/{me!.Id}/deactivate"), content: null);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "user.cannot_deactivate_self");
    }

    [Fact]
    public async Task Unknown_accounts_return_404()
    {
        var id = Guid.NewGuid();

        using var get = await _owner.GetAsync(Url($"/api/users/{id}"));
        using var role = await _owner.PutAsJsonAsync(Url($"/api/users/{id}/role"), new { role = "Mechanic" });

        await AssertProblemAsync(get, HttpStatusCode.NotFound, "user.not_found");
        await AssertProblemAsync(role, HttpStatusCode.NotFound, "user.not_found");
    }

    public void Dispose() => _owner.Dispose();

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@autoflow.test";

    private Task<HttpResponseMessage> CreateAsync(string email, string role, string password = ApiDatabaseFixture.StaffPassword) =>
        _owner.PostAsJsonAsync(Url("/api/users"), new { email, displayName = "New Colleague", role, password });

    private async Task<UserDetails> CreateUserAsync(string email, string role)
    {
        using var response = await CreateAsync(email, role);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDetails>())!;
    }

    private async Task<HttpResponseMessage> GetMeAsync(string token)
    {
        using var client = _api.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/auth/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
