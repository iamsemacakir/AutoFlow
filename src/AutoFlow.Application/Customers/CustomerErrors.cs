using AutoFlow.Application.Common;

namespace AutoFlow.Application.Customers;

public static class CustomerErrors
{
    public static Error NotFound(Guid customerId) =>
        Error.NotFound("customer.not_found", $"Customer '{customerId}' was not found.");
}
