using AutoFlow.Domain.Customers;

namespace AutoFlow.Application.Customers;

/// <summary>Read model of a customer returned by the customer use cases.</summary>
public sealed record CustomerDetails(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    DateTimeOffset CreatedAt)
{
    public static CustomerDetails From(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email.Value,
            customer.PhoneNumber,
            customer.CreatedAt);
    }
}
