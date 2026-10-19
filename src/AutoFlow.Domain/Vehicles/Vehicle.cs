using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Vehicles;

/// <summary>
/// A vehicle registered with the workshop. Each vehicle belongs to exactly one
/// customer; ownership can be transferred when a car is sold.
/// </summary>
public sealed class Vehicle : Entity
{
    public const int MakeModelMaxLength = 60;

    /// <summary>Year of the Benz Patent-Motorwagen; nothing older is a car.</summary>
    public const int EarliestModelYear = 1886;

    private Vehicle(
        Guid id,
        Guid customerId,
        Vin vin,
        LicensePlate licensePlate,
        string make,
        string model,
        int modelYear,
        int mileageKm,
        DateTimeOffset registeredAt)
        : base(id)
    {
        CustomerId = customerId;
        Vin = vin;
        LicensePlate = licensePlate;
        Make = make;
        Model = model;
        ModelYear = modelYear;
        MileageKm = mileageKm;
        RegisteredAt = registeredAt;
    }

    public Guid CustomerId { get; private set; }

    public Vin Vin { get; }

    public LicensePlate LicensePlate { get; private set; }

    public string Make { get; }

    public string Model { get; }

    public int ModelYear { get; }

    /// <summary>Last recorded odometer reading in kilometres.</summary>
    public int MileageKm { get; private set; }

    public DateTimeOffset RegisteredAt { get; }

    public static Vehicle Register(
        Guid customerId,
        string vin,
        string licensePlate,
        string make,
        string model,
        int modelYear,
        int mileageKm,
        DateTimeOffset registeredAt)
    {
        EnsureCustomer(customerId);

        // Manufacturers sell next year's model from roughly mid-year.
        var latestModelYear = registeredAt.Year + 1;
        if (modelYear < EarliestModelYear || modelYear > latestModelYear)
        {
            throw new DomainException(
                $"Model year must be between {EarliestModelYear} and {latestModelYear}.");
        }

        EnsureNonNegative(mileageKm);

        return new Vehicle(
            Guid.NewGuid(),
            customerId,
            Vin.Create(vin),
            LicensePlate.Create(licensePlate),
            Guard.RequiredText(make, "Make", MakeModelMaxLength),
            Guard.RequiredText(model, "Model", MakeModelMaxLength),
            modelYear,
            mileageKm,
            registeredAt);
    }

    /// <summary>
    /// Records a new odometer reading. Readings may never go backwards; a
    /// lower value usually means a typo or odometer tampering.
    /// </summary>
    public void RecordMileage(int mileageKm)
    {
        EnsureNonNegative(mileageKm);

        if (mileageKm < MileageKm)
        {
            throw new DomainException(
                $"Mileage cannot decrease (current {MileageKm} km, new {mileageKm} km).");
        }

        MileageKm = mileageKm;
    }

    public void ChangeLicensePlate(string licensePlate) =>
        LicensePlate = LicensePlate.Create(licensePlate);

    public void TransferTo(Guid newCustomerId)
    {
        EnsureCustomer(newCustomerId);

        if (newCustomerId == CustomerId)
        {
            throw new DomainException("Vehicle already belongs to this customer.");
        }

        CustomerId = newCustomerId;
    }

    private static void EnsureCustomer(Guid customerId)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainException("A vehicle must belong to a customer.");
        }
    }

    private static void EnsureNonNegative(int mileageKm)
    {
        if (mileageKm < 0)
        {
            throw new DomainException("Mileage cannot be negative.");
        }
    }
}
