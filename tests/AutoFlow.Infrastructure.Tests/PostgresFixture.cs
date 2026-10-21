using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>
/// Starts one throw-away PostgreSQL container for the whole test run (Docker
/// required) and creates the schema from the EF Core model. Tests isolate
/// themselves by using fresh ids and VINs instead of truncating tables.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>A new context, i.e. a new "request": nothing is tracked yet.</summary>
    public AutoFlowDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AutoFlowDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
