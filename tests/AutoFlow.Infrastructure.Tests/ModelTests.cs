using AutoFlow.Domain.Vehicles;
using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>Checks the EF Core model itself; no database connection is opened.</summary>
public class ModelTests
{
    private static AutoFlowDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AutoFlowDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options);

    [Fact]
    public void Vin_has_a_unique_index()
    {
        using var db = CreateContext();
        var vehicle = db.Model.FindEntityType(typeof(Vehicle))!;

        var index = Assert.Single(vehicle.GetIndexes(), i => i.Properties.Single().Name == nameof(Vehicle.Vin));

        Assert.True(index.IsUnique);
        Assert.Equal("ux_vehicles_vin", index.GetDatabaseName());
    }

    [Fact]
    public void Tables_and_columns_use_snake_case()
    {
        using var db = CreateContext();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            Assert.Matches("^[a-z_]+$", entity.GetTableName());
            foreach (var property in entity.GetProperties())
            {
                Assert.Matches("^[a-z_]+$", property.GetColumnName());
            }
        }
    }

    [Fact]
    public void Every_entity_has_an_xmin_concurrency_token()
    {
        using var db = CreateContext();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var token = Assert.Single(entity.GetProperties(), p => p.IsConcurrencyToken);
            Assert.Equal("xmin", token.GetColumnName());
        }
    }
}
