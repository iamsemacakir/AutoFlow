using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoFlow.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use-case handlers and the system <see cref="TimeProvider"/>.
    /// Repository and unit-of-work implementations are registered by the
    /// infrastructure layer.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<RegisterCustomerHandler>();
        services.AddScoped<UpdateCustomerHandler>();
        services.AddScoped<RegisterVehicleHandler>();
        services.AddScoped<RecordMileageHandler>();
        services.AddScoped<TransferVehicleHandler>();

        return services;
    }
}
