using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Tests.Fakes;

internal static class TestData
{
    public const string Vin = "WVWZZZ1JZXW000001";

    public static Customer Customer(string firstName = "Anna", string lastName = "Schmidt") =>
        Domain.Customers.Customer.Create(firstName, lastName, $"{firstName}@example.com", null, FixedTimeProvider.Default);

    public static Vehicle Vehicle(Guid customerId, string vin = Vin, int mileageKm = 50_000) =>
        Domain.Vehicles.Vehicle.Register(customerId, vin, "M-AB 123", "Volkswagen", "Golf", 2019, mileageKm, FixedTimeProvider.Default);
}
