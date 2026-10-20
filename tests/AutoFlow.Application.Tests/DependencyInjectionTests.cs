using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Vehicles;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.Application.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_resolves_every_handler_once_infrastructure_is_registered()
    {
        var store = new InMemoryStore();
        var services = new ServiceCollection()
            .AddSingleton<ICustomerRepository>(store)
            .AddSingleton<IVehicleRepository>(store)
            .AddSingleton<IUnitOfWork>(store)
            .AddApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegisterCustomerHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UpdateCustomerHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegisterVehicleHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RecordMileageHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TransferVehicleHandler>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddApplication_keeps_a_previously_registered_time_provider()
    {
        var fixedTime = new FixedTimeProvider();
        var services = new ServiceCollection().AddSingleton<TimeProvider>(fixedTime).AddApplication();

        using var provider = services.BuildServiceProvider();

        Assert.Same(fixedTime, provider.GetRequiredService<TimeProvider>());
    }
}
