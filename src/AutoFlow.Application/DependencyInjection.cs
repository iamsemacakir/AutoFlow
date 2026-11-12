using AutoFlow.Application.Appointments;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
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
        services.AddScoped<DeleteCustomerHandler>();
        services.AddScoped<GetCustomerHandler>();
        services.AddScoped<ListCustomersHandler>();
        services.AddScoped<RegisterVehicleHandler>();
        services.AddScoped<RecordMileageHandler>();
        services.AddScoped<TransferVehicleHandler>();
        services.AddScoped<GetVehicleHandler>();
        services.AddScoped<ListCustomerVehiclesHandler>();
        services.AddScoped<OpenWorkOrderHandler>();
        services.AddScoped<AddLabourLineHandler>();
        services.AddScoped<AddPartLineHandler>();
        services.AddScoped<RemoveWorkOrderLineHandler>();
        services.AddScoped<ApproveWorkOrderHandler>();
        services.AddScoped<StartWorkOrderHandler>();
        services.AddScoped<CompleteWorkOrderHandler>();
        services.AddScoped<CancelWorkOrderHandler>();
        services.AddScoped<GetWorkOrderHandler>();
        services.AddScoped<ListVehicleWorkOrdersHandler>();
        services.AddScoped<CreateBayHandler>();
        services.AddScoped<RenameBayHandler>();
        services.AddScoped<TakeBayOutOfServiceHandler>();
        services.AddScoped<ReturnBayToServiceHandler>();
        services.AddScoped<GetBayHandler>();
        services.AddScoped<ListBaysHandler>();
        services.AddScoped<BookAppointmentHandler>();
        services.AddScoped<RescheduleAppointmentHandler>();
        services.AddScoped<CheckInAppointmentHandler>();
        services.AddScoped<CancelAppointmentHandler>();
        services.AddScoped<MarkAppointmentNoShowHandler>();
        services.AddScoped<GetAppointmentHandler>();

        return services;
    }
}
