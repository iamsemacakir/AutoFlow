using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Customers;

namespace AutoFlow.Application.Customers;

public sealed record RegisterCustomerCommand(
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber);

/// <summary>Registers a new customer; the creation time comes from <see cref="TimeProvider"/>.</summary>
public sealed class RegisterCustomerHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<CustomerDetails>> HandleAsync(
        RegisterCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = Customer.Create(
            command.FirstName,
            command.LastName,
            command.Email,
            command.PhoneNumber,
            timeProvider.GetUtcNow());

        customers.Add(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CustomerDetails.From(customer));
    }
}
