using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>Checks that the migrations match the model; no database connection is opened.</summary>
public class MigrationTests
{
    private static AutoFlowDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AutoFlowDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options);

    [Fact]
    public void Model_has_no_changes_missing_from_the_migrations()
    {
        using var db = CreateContext();

        // Fails when a mapping changes without `dotnet ef migrations add`.
        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The EF Core model differs from the latest migration snapshot. Add a migration.");
    }

    [Fact]
    public void Initial_migration_is_the_first_migration()
    {
        using var db = CreateContext();

        var first = db.Database.GetMigrations().First();

        Assert.EndsWith("_InitialCreate", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotent_script_creates_the_tables_and_constraints()
    {
        using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();

        var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("CREATE TABLE customers", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE vehicles", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX ux_vehicles_vin", script, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT fk_vehicles_customers_customer_id", script, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT ck_vehicles_mileage_km_non_negative", script, StringComparison.Ordinal);
        // xmin is a PostgreSQL system column and must never be created explicitly.
        Assert.DoesNotContain("xmin xid", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Design_time_factory_falls_back_to_the_local_compose_database(string? configured)
    {
        Assert.Equal(
            AutoFlowDbContextFactory.LocalConnectionString,
            AutoFlowDbContextFactory.ResolveConnectionString(configured));
    }

    [Fact]
    public void Design_time_factory_prefers_the_configured_connection_string()
    {
        const string configured = "Host=db.example;Database=autoflow";

        Assert.Equal(configured, AutoFlowDbContextFactory.ResolveConnectionString(configured));
    }
}

[Collection(PostgresDefinition.Name)]
public sealed class AppliedMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task All_migrations_are_applied_to_the_test_database()
    {
        await using var db = postgres.CreateContext();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        var pending = await db.Database.GetPendingMigrationsAsync();

        Assert.Equal(db.Database.GetMigrations(), applied);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migrating_an_up_to_date_database_is_a_no_op()
    {
        await using var db = postgres.CreateContext();

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
