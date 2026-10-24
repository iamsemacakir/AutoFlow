using AutoFlow.Application.Common;

namespace AutoFlow.Application.Customers;

public static class CustomerErrors
{
    public static Error NotFound(Guid customerId) =>
        Error.NotFound("customer.not_found", $"Customer '{customerId}' was not found.");

    public static Error HasVehicles(Guid customerId) =>
        Error.Conflict(
            "customer.has_vehicles",
            $"Customer '{customerId}' still owns vehicles. Transfer them to another customer first.");

    public static Error SearchTooLong(int maxLength) =>
        Error.Validation("customer.search_too_long", $"The search text must be at most {maxLength} characters.");
}
