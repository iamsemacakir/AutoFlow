using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.Tests.Fakes;

internal static class TestData
{
    public const string Vin = "WVWZZZ1JZXW000001";

    public static Customer Customer(string firstName = "Anna", string lastName = "Schmidt") =>
        Domain.Customers.Customer.Create(firstName, lastName, $"{firstName}@example.com", null, FixedTimeProvider.Default);

    public static Vehicle Vehicle(Guid customerId, string vin = Vin, int mileageKm = 50_000) =>
        Domain.Vehicles.Vehicle.Register(customerId, vin, "M-AB 123", "Volkswagen", "Golf", 2019, mileageKm, FixedTimeProvider.Default);

    public static WorkOrder WorkOrder(Vehicle vehicle, DateTimeOffset? createdAt = null) =>
        Domain.WorkOrders.WorkOrder.Open(
            vehicle.Id,
            vehicle.CustomerId,
            "Squeaking brakes at the front",
            vehicle.MileageKm,
            createdAt ?? FixedTimeProvider.Default);

    public static Bay Bay(string name = "Lift 1") => Domain.Bays.Bay.Create(name, FixedTimeProvider.Default);

    /// <summary>
    /// A slot on the day after <see cref="FixedTimeProvider.Default"/>, so it
    /// is never in the past: <c>Slot(9, 2)</c> is 09:00–11:00 UTC.
    /// </summary>
    public static AppointmentSlot Slot(double startHour, double hours) =>
        AppointmentSlot.StartingAt(
            new DateTimeOffset(FixedTimeProvider.Default.Date.AddDays(1), TimeSpan.Zero).AddHours(startHour),
            TimeSpan.FromHours(hours));

    public static Appointment Appointment(Bay bay, Vehicle vehicle, AppointmentSlot slot) =>
        Domain.Appointments.Appointment.Book(
            bay.Id,
            vehicle.Id,
            vehicle.CustomerId,
            slot,
            "Annual service",
            FixedTimeProvider.Default);
}
