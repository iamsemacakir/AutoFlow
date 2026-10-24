using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Customers;

public sealed record GetCustomerQuery(Guid CustomerId);

/// <summary>Looks up a single customer by id.</summary>
public sealed class GetCustomerHandler(ICustomerRepository customers)
{
    public async Task<Result<CustomerDetails>> HandleAsync(
        GetCustomerQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var customer = await customers.GetByIdAsync(query.CustomerId, cancellationToken);

        return customer is null
            ? Result.Failure<CustomerDetails>(CustomerErrors.NotFound(query.CustomerId))
            : Result.Success(CustomerDetails.From(customer));
    }
}
