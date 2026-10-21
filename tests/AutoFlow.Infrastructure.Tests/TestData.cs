using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Infrastructure.Tests;

internal static class TestData
{
    /// <summary>Whole seconds: PostgreSQL stores microseconds, .NET ticks are finer.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 21, 7, 45, 0, TimeSpan.Zero);

    // VIN alphabet without I, O and Q.
    private const string VinAlphabet = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";

    /// <summary>A random valid VIN so tests sharing one database never collide.</summary>
    public static string UniqueVin() =>
        "WVW" + string.Concat(Enumerable.Range(0, Vin.Length - 3)
            .Select(_ => VinAlphabet[Random.Shared.Next(VinAlphabet.Length)]));

    public static Customer Customer(string? phoneNumber = null) =>
        Domain.Customers.Customer.Create("Anna", "Schmidt", "Anna.Schmidt@Example.com", phoneNumber, Now);

    public static Vehicle Vehicle(Guid customerId, string? vin = null, DateTimeOffset? registeredAt = null) =>
        Domain.Vehicles.Vehicle.Register(
            customerId,
            vin ?? UniqueVin(),
            "m-ab  123",
            "Volkswagen",
            "Golf",
            2019,
            50_000,
            registeredAt ?? Now);
}
