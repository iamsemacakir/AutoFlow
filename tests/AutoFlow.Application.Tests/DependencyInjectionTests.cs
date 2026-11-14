using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
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
            .AddSingleton<IWorkOrderRepository>(store)
            .AddSingleton<IBayRepository>(store)
            .AddSingleton<IAppointmentRepository>(store)
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
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DeleteCustomerHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetCustomerHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ListCustomersHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegisterVehicleHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RecordMileageHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TransferVehicleHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetVehicleHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ListCustomerVehiclesHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OpenWorkOrderHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AddLabourLineHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AddPartLineHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RemoveWorkOrderLineHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ApproveWorkOrderHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<StartWorkOrderHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CompleteWorkOrderHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CancelWorkOrderHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetWorkOrderHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ListVehicleWorkOrdersHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CreateBayHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RenameBayHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TakeBayOutOfServiceHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ReturnBayToServiceHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetBayHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ListBaysHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<BookAppointmentHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RescheduleAppointmentHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CheckInAppointmentHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CancelAppointmentHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MarkAppointmentNoShowHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetAppointmentHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetDayScheduleHandler>());
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
