using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoFlow.Application.Users;
using AutoFlow.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AutoFlow.Api.Tests;

/// <summary>
/// Hosts the real API (all layers, no fakes) against a throw-away PostgreSQL
/// container (Docker required). The API builds the schema itself on start
/// (<c>Database:MigrateOnStartup</c>) and creates the initial owner from
/// configuration, exactly as a fresh installation does; the fixture then
/// opens one account per role and signs each in once. Tests share the
/// database and isolate themselves with fresh data.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed in IAsyncLifetime.DisposeAsync, which xUnit calls for fixtures.")]
public sealed class ApiDatabaseFixture : IAsyncLifetime
{
    /// <summary>The password of every account the fixture opens.</summary>
    public const string StaffPassword = "fixture password for every role";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly Dictionary<UserRole, string> _tokens = [];

    private WebApplicationFactory<Program>? _root;
    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <summary>The login of the fixture's account with <paramref name="role"/>.</summary>
    public static string EmailFor(UserRole role) => $"{role.ToString().ToLowerInvariant()}@autoflow.test";

    /// <summary>A client that sends the token of the fixture's account with <paramref name="role"/>.</summary>
    public HttpClient CreateClient(UserRole role = UserRole.Owner)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = AuthorizationFor(role);
        return client;
    }

    public AuthenticationHeaderValue AuthorizationFor(UserRole role) => new("Bearer", _tokens[role]);

    /// <summary>
    /// The connection string of another, still empty database in the same
    /// container, for tests that need a fresh installation.
    /// </summary>
    public string ConnectionStringForNewDatabase() =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = $"autoflow_{Guid.NewGuid():N}",
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _root = new WebApplicationFactory<Program>();
        _factory = _root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AutoFlow", _postgres.GetConnectionString());
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Auth:InitialOwner:Email", EmailFor(UserRole.Owner));
            builder.UseSetting("Auth:InitialOwner:DisplayName", "Olga Owner");
            builder.UseSetting("Auth:InitialOwner:Password", StaffPassword);

            // Long enough to outlast tests that move the clock forward by days.
            builder.UseSetting("Auth:TokenLifetime", "30.00:00:00");
        });

        using var client = _factory.CreateClient();
        _tokens[UserRole.Owner] = await SignInAsync(client, EmailFor(UserRole.Owner));
        client.DefaultRequestHeaders.Authorization = AuthorizationFor(UserRole.Owner);

        foreach (var (role, name) in new[] { (UserRole.ServiceAdvisor, "Sam Advisor"), (UserRole.Mechanic, "Max Mechanic") })
        {
            using var created = await client.PostAsJsonAsync(
                new Uri("/api/users", UriKind.Relative),
                new { email = EmailFor(role), displayName = name, role = role.ToString(), password = StaffPassword });
            created.EnsureSuccessStatusCode();
            _tokens[role] = await SignInAsync(client, EmailFor(role));
        }
    }

    public async Task DisposeAsync()
    {
        if (_root is not null)
        {
            await _root.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>Signs in through the API and returns the bearer token.</summary>
    public static async Task<string> SignInAsync(HttpClient client, string email, string password = StaffPassword)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/token", UriKind.Relative),
            new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SignedIn>())!.AccessToken;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiDatabaseDefinition : ICollectionFixture<ApiDatabaseFixture>
{
    public const string Name = "API with PostgreSQL";
}
