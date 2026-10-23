using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using AutoFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.Api.Tests;

/// <summary>
/// The API host composes the application and infrastructure layers. Resolving
/// services does not open a database connection, so no PostgreSQL is needed.
/// </summary>
public class CompositionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(typeof(RegisterCustomerHandler))]
    [InlineData(typeof(UpdateCustomerHandler))]
    [InlineData(typeof(RegisterVehicleHandler))]
    [InlineData(typeof(RecordMileageHandler))]
    [InlineData(typeof(TransferVehicleHandler))]
    [InlineData(typeof(IUnitOfWork))]
    public void Use_case_handlers_and_persistence_resolve_per_request(Type service)
    {
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(service));
    }

    [Fact]
    public void Unit_of_work_is_the_request_scoped_db_context()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Same(
            scope.ServiceProvider.GetRequiredService<AutoFlowDbContext>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }
}
