using AutoFlow.Application.Abstractions;
using AutoFlow.Infrastructure.Documents;
using AutoFlow.Infrastructure.Persistence;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="AutoFlowDbContext"/> on PostgreSQL and the
    /// repository and unit-of-work implementations the application layer
    /// depends on. All are scoped, so one request shares one context. The
    /// invoice PDF renderer holds no state and is a singleton; it takes the
    /// <see cref="TimeProvider"/> the application layer registers.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AutoFlowDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AutoFlowDbContext>());
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
        services.AddScoped<IBayRepository, BayRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();

        services.AddSingleton<IInvoiceDocumentRenderer, PdfInvoiceRenderer>();

        return services;
    }
}
