using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Customers;

public sealed record UpdateCustomerCommand(
    Guid CustomerId,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber);

/// <summary>Replaces a customer's name and contact details.</summary>
public sealed class UpdateCustomerHandler(ICustomerRepository customers, IUnitOfWork unitOfWork)
{
    public async Task<Result<CustomerDetails>> HandleAsync(
        UpdateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await customers.GetByIdAsync(command.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<CustomerDetails>(CustomerErrors.NotFound(command.CustomerId));
        }

        customer.Rename(command.FirstName, command.LastName);
        customer.UpdateContactDetails(command.Email, command.PhoneNumber);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CustomerDetails.From(customer));
    }
}
