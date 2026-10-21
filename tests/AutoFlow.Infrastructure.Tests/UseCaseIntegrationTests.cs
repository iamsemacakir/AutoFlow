using AutoFlow.Application;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>
/// Runs the real use-case handlers against PostgreSQL through the same DI
/// registrations the API will use, one scope per simulated request.
/// </summary>
[Collection(PostgresDefinition.Name)]
public sealed class UseCaseIntegrationTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public UseCaseIntegrationTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        _provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now))
            .AddApplication()
            .AddInfrastructure(postgres.ConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task Customer_and_vehicle_registered_in_separate_requests_are_linked()
    {
        var customer = await InScope<RegisterCustomerHandler, CustomerDetails>(handler =>
            handler.HandleAsync(new RegisterCustomerCommand("Jonas", "Becker", "jonas@example.com", null)));

        var vin = TestData.UniqueVin();
        var vehicle = await InScope<RegisterVehicleHandler, VehicleDetails>(handler =>
            handler.HandleAsync(new RegisterVehicleCommand(customer.Id, vin, "B-JB 42", "BMW", "320d", 2021, 30_000)));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoFlowDbContext>();
        var stored = await db.Vehicles.SingleAsync(v => v.Id == vehicle.Id);

        Assert.Equal(customer.Id, stored.CustomerId);
        Assert.Equal(vin, stored.Vin.Value);
        Assert.Equal(TestData.Now, stored.RegisteredAt);
    }

    [Fact]
    public async Task Registering_a_known_vin_again_returns_a_conflict()
    {
        var customer = await InScope<RegisterCustomerHandler, CustomerDetails>(handler =>
            handler.HandleAsync(new RegisterCustomerCommand("Mia", "Wagner", "mia@example.com", null)));
        var command = new RegisterVehicleCommand(customer.Id, TestData.UniqueVin(), "K-MW 7", "Opel", "Corsa", 2018, 80_000);
        await InScope<RegisterVehicleHandler, VehicleDetails>(handler => handler.HandleAsync(command));

        using var scope = _provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RegisterVehicleHandler>().HandleAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.duplicate_vin", result.Error.Code);
    }

    public void Dispose() => _provider.Dispose();

    private async Task<TOut> InScope<THandler, TOut>(Func<THandler, Task<Result<TOut>>> call)
        where THandler : notnull
    {
        using var scope = _provider.CreateScope();
        var result = await call(scope.ServiceProvider.GetRequiredService<THandler>());
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
