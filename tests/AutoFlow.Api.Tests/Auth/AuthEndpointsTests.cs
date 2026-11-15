using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoFlow.Application.Users;
using AutoFlow.Domain.Users;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Auth;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class AuthEndpointsTests : IDisposable
{
    private readonly ApiDatabaseFixture _api;
    private readonly HttpClient _anonymous;

    public AuthEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _anonymous = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Sign_in_returns_a_bearer_token_for_the_account()
    {
        using var response = await _anonymous.PostAsJsonAsync(
            Url("/api/auth/token"),
            new { email = "  MECHANIC@autoflow.test ", password = ApiDatabaseFixture.StaffPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var signedIn = await response.Content.ReadFromJsonAsync<SignedIn>();
        Assert.NotNull(signedIn);
        Assert.Equal("Bearer", signedIn.TokenType);
        Assert.Equal(3, signedIn.AccessToken.Split('.').Length);
        Assert.True(signedIn.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(("mechanic@autoflow.test", "Mechanic", true), (signedIn.User.Email, signedIn.User.Role, signedIn.User.IsActive));
        Assert.NotNull(signedIn.User.LastSignInAt);

        using var me = await GetMeAsync(signedIn.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("Max Mechanic", (await me.Content.ReadFromJsonAsync<UserDetails>())!.DisplayName);
    }

    [Theory]
    [InlineData("owner@autoflow.test", "not the password")]
    [InlineData("nobody@autoflow.test", ApiDatabaseFixture.StaffPassword)]
    public async Task Sign_in_with_wrong_credentials_returns_401(string email, string password)
    {
        using var response = await _anonymous.PostAsJsonAsync(Url("/api/auth/token"), new { email, password });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Sign_in_without_credentials_returns_400()
    {
        using var response = await _anonymous.PostAsJsonAsync(Url("/api/auth/token"), new { email = "", password = "" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Request_without_a_token_returns_401()
    {
        using var response = await _anonymous.GetAsync(Url("/api/auth/me"));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl")]
    public async Task Request_with_an_invalid_token_returns_401(string token)
    {
        using var response = await GetMeAsync(token);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task Token_with_a_tampered_role_is_rejected()
    {
        var token = await ApiDatabaseFixture.SignInAsync(_anonymous, ApiDatabaseFixture.EmailFor(UserRole.Mechanic));
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[1])).Replace("Mechanic", "Owner", StringComparison.Ordinal);
        var forged = $"{parts[0]}.{Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(payload))}.{parts[2]}";

        using var response = await GetMeAsync(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_change_their_own_password_with_the_current_one()
    {
        var email = await CreateUserAsync(UserRole.ServiceAdvisor);
        var token = await ApiDatabaseFixture.SignInAsync(_anonymous, email);

        using var wrongCurrent = await ChangePasswordAsync(token, "not my password", "a brand new password");
        using var tooShort = await ChangePasswordAsync(token, ApiDatabaseFixture.StaffPassword, "short");
        using var changed = await ChangePasswordAsync(token, ApiDatabaseFixture.StaffPassword, "a brand new password");

        await AssertProblemAsync(wrongCurrent, HttpStatusCode.BadRequest, "auth.wrong_current_password");
        await AssertProblemAsync(tooShort, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var oldPassword = await _anonymous.PostAsJsonAsync(
            Url("/api/auth/token"),
            new { email, password = ApiDatabaseFixture.StaffPassword });
        await AssertProblemAsync(oldPassword, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        Assert.NotEmpty(await ApiDatabaseFixture.SignInAsync(_anonymous, email, "a brand new password"));
    }

    public void Dispose() => _anonymous.Dispose();

    private async Task<string> CreateUserAsync(UserRole role)
    {
        using var owner = _api.CreateClient(UserRole.Owner);
        var email = $"user-{Guid.NewGuid():N}@autoflow.test";
        using var response = await owner.PostAsJsonAsync(
            Url("/api/users"),
            new { email, displayName = "Test User", role = role.ToString(), password = ApiDatabaseFixture.StaffPassword });
        response.EnsureSuccessStatusCode();
        return email;
    }

    private async Task<HttpResponseMessage> GetMeAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/auth/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _anonymous.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ChangePasswordAsync(string token, string currentPassword, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/auth/me/password"))
        {
            Content = JsonContent.Create(new { currentPassword, newPassword }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _anonymous.SendAsync(request);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
