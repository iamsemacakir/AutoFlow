using AutoFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AutoFlow.Api.Tests;

/// <summary>
/// Hosts the real API (all layers, no fakes) against a throw-away PostgreSQL
/// container (Docker required) whose schema is built from the migrations.
/// Tests share the database and isolate themselves with fresh data.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed in IAsyncLifetime.DisposeAsync, which xUnit calls for fixtures.")]
public sealed class ApiDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private WebApplicationFactory<Program>? _root;
    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _root = new WebApplicationFactory<Program>();
        _factory = _root.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:AutoFlow", _postgres.GetConnectionString()));

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AutoFlowDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_root is not null)
        {
            await _root.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiDatabaseDefinition : ICollectionFixture<ApiDatabaseFixture>
{
    public const string Name = "API with PostgreSQL";
}
