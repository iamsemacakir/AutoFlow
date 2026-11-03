using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Customers;

public sealed record DeleteCustomerCommand(Guid CustomerId);

/// <summary>
/// Deletes a customer who owns no vehicles and never commissioned a work
/// order. Vehicles carry the service history, so they have to be transferred
/// to another customer first; work orders stay with the customer they were
/// opened for (and will be invoiced to), so a customer who has any is kept.
/// The handler refuses instead of deleting either along with their owner.
/// Returns the deleted customer.
/// </summary>
public sealed class DeleteCustomerHandler(
    ICustomerRepository customers,
    IVehicleRepository vehicles,
    IWorkOrderRepository workOrders,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CustomerDetails>> HandleAsync(
        DeleteCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await customers.GetByIdAsync(command.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<CustomerDetails>(CustomerErrors.NotFound(command.CustomerId));
        }

        if (await vehicles.ExistsForCustomerAsync(customer.Id, cancellationToken))
        {
            return Result.Failure<CustomerDetails>(CustomerErrors.HasVehicles(customer.Id));
        }

        if (await workOrders.ExistsForCustomerAsync(customer.Id, cancellationToken))
        {
            return Result.Failure<CustomerDetails>(CustomerErrors.HasWorkOrders(customer.Id));
        }

        customers.Remove(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CustomerDetails.From(customer));
    }
}
