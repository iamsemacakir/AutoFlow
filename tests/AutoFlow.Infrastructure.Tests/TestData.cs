using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;

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

    public static WorkOrder WorkOrder(Guid vehicleId, Guid customerId) =>
        Domain.WorkOrders.WorkOrder.Open(vehicleId, customerId, "Squeaking brakes at the front", 62_500, Now);

    /// <summary>A bay with a random name so tests sharing one database never collide.</summary>
    public static Bay Bay(string? name = null) =>
        Domain.Bays.Bay.Create(name ?? UniqueBayName(), Now);

    public static string UniqueBayName() => $"Lift {Guid.NewGuid():N}";

    /// <summary>
    /// A slot on the day after <see cref="Now"/>, so bookings are never in the
    /// past: <c>Slot(9, 2)</c> is 09:00–11:00 UTC.
    /// </summary>
    public static AppointmentSlot Slot(double startHour, double hours) =>
        AppointmentSlot.StartingAt(
            new DateTimeOffset(Now.Date.AddDays(1), TimeSpan.Zero).AddHours(startHour),
            TimeSpan.FromHours(hours));

    public static Appointment Appointment(Guid bayId, Guid vehicleId, Guid customerId, AppointmentSlot slot) =>
        Domain.Appointments.Appointment.Book(bayId, vehicleId, customerId, slot, "Annual service", Now);

    /// <summary>A work order with 239.50 net of labour and parts, taken through to completion.</summary>
    public static WorkOrder CompletedWorkOrder(Guid vehicleId, Guid customerId)
    {
        var workOrder = WorkOrder(vehicleId, customerId);
        workOrder.AddLabour("Replace front pads and discs", 1.5m, 95m);
        workOrder.AddPart("0986494", "Brake disc, front", 2m, 48.5m);
        workOrder.Approve(Now.AddHours(1));
        workOrder.Start(Now.AddHours(2));
        workOrder.Complete(Now.AddHours(4));
        return workOrder;
    }

    private static int _invoiceYear = 3000;

    /// <summary>
    /// A year no other test issues invoices in, so sequences never collide in
    /// the shared database and "the last number of the year" is predictable.
    /// </summary>
    public static int UniqueInvoiceYear() => Interlocked.Increment(ref _invoiceYear);

    /// <summary>An invoice with 19 % VAT (285.01 gross), issued on 1 June of <paramref name="year"/>.</summary>
    public static Invoice Invoice(WorkOrder workOrder, int year, int sequence = 1) =>
        Domain.Invoices.Invoice.IssueFor(
            workOrder,
            sequence,
            "Anna Schmidt",
            "Volkswagen Golf (2019), M-AB 123",
            19m,
            14,
            new DateTimeOffset(year, 6, 1, 9, 0, 0, TimeSpan.Zero));
}
