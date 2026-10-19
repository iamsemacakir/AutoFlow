using AutoFlow.Domain.Common;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Domain.Tests.Vehicles;

public class VehicleTests
{
    private const string ValidVin = "WVWZZZ1JZXW000001";
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();

    private static Vehicle NewVehicle(int mileageKm = 50_000, int modelYear = 2019) =>
        Vehicle.Register(Owner, ValidVin, "m-ab 123", "Volkswagen", "Golf", modelYear, mileageKm, Now);

    [Fact]
    public void Register_creates_vehicle_with_normalized_values()
    {
        var vehicle = NewVehicle();

        Assert.NotEqual(Guid.Empty, vehicle.Id);
        Assert.Equal(Owner, vehicle.CustomerId);
        Assert.Equal(ValidVin, vehicle.Vin.Value);
        Assert.Equal("M-AB 123", vehicle.LicensePlate.Value);
        Assert.Equal("Volkswagen", vehicle.Make);
        Assert.Equal("Golf", vehicle.Model);
        Assert.Equal(2019, vehicle.ModelYear);
        Assert.Equal(50_000, vehicle.MileageKm);
        Assert.Equal(Now, vehicle.RegisteredAt);
    }

    [Fact]
    public void Register_requires_customer()
    {
        Assert.Throws<DomainException>(() =>
            Vehicle.Register(Guid.Empty, ValidVin, "M-AB 123", "VW", "Golf", 2019, 0, Now));
    }

    [Theory]
    [InlineData(1885)]
    [InlineData(2028)] // registration year 2026 + 2
    public void Register_rejects_implausible_model_year(int modelYear)
    {
        Assert.Throws<DomainException>(() => NewVehicle(modelYear: modelYear));
    }

    [Fact]
    public void Register_accepts_next_years_model()
    {
        Assert.Equal(2027, NewVehicle(modelYear: 2027).ModelYear);
    }

    [Fact]
    public void Register_rejects_negative_mileage()
    {
        Assert.Throws<DomainException>(() => NewVehicle(mileageKm: -1));
    }

    [Theory]
    [InlineData("", "Golf")]
    [InlineData("VW", " ")]
    public void Register_requires_make_and_model(string make, string model)
    {
        Assert.Throws<DomainException>(() =>
            Vehicle.Register(Owner, ValidVin, "M-AB 123", make, model, 2019, 0, Now));
    }

    [Fact]
    public void RecordMileage_accepts_equal_or_higher_reading()
    {
        var vehicle = NewVehicle(mileageKm: 50_000);

        vehicle.RecordMileage(50_000);
        vehicle.RecordMileage(61_250);

        Assert.Equal(61_250, vehicle.MileageKm);
    }

    [Fact]
    public void RecordMileage_rejects_lower_reading()
    {
        var vehicle = NewVehicle(mileageKm: 50_000);

        var ex = Assert.Throws<DomainException>(() => vehicle.RecordMileage(49_999));
        Assert.Contains("cannot decrease", ex.Message, StringComparison.Ordinal);
        Assert.Equal(50_000, vehicle.MileageKm);
    }

    [Fact]
    public void ChangeLicensePlate_normalizes_new_plate()
    {
        var vehicle = NewVehicle();

        vehicle.ChangeLicensePlate("b-xy 9");

        Assert.Equal("B-XY 9", vehicle.LicensePlate.Value);
    }

    [Fact]
    public void TransferTo_changes_owner()
    {
        var vehicle = NewVehicle();
        var newOwner = Guid.NewGuid();

        vehicle.TransferTo(newOwner);

        Assert.Equal(newOwner, vehicle.CustomerId);
    }

    [Fact]
    public void TransferTo_rejects_same_or_empty_owner()
    {
        var vehicle = NewVehicle();

        Assert.Throws<DomainException>(() => vehicle.TransferTo(Owner));
        Assert.Throws<DomainException>(() => vehicle.TransferTo(Guid.Empty));
    }
}
