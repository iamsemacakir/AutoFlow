using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Invoices;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.Users;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;
using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Api.Hosting;

/// <summary>Settings of the demo workshop, bound from the <c>Seed</c> configuration section.</summary>
public sealed class DemoDataOptions
{
    public const string SectionName = "Seed";

    /// <summary>Fill an empty database with a demo workshop on start.</summary>
    public bool DemoData { get; init; }

    /// <summary>The time zone the demo day is planned in.</summary>
    public string TimeZone { get; init; } = "Europe/Berlin";

    /// <summary>The password of the demo service advisor and mechanic.</summary>
    public string StaffPassword { get; init; } = "autoflow-demo";
}

/// <summary>
/// Fills an empty database with a believable workshop so the application can
/// be tried out at once: staff for every role, bays (one out of service),
/// customers with vehicles, today's and tomorrow's appointments in every
/// status, work orders from estimate to invoiced, and invoices that are open,
/// overdue and paid. The day is planned around the moment of seeding, in
/// <see cref="DemoDataOptions.TimeZone"/>. Nothing happens once a customer
/// exists, so a real installation is never touched. All names, e-mail
/// addresses and vehicles are invented.
/// </summary>
internal sealed class DemoDataSeeder(
    AutoFlowDbContext db,
    IPasswordHasher passwordHasher,
    InvoicingSettings invoicing,
    TimeProvider timeProvider)
{
    private readonly Dictionary<int, int> _nextInvoiceSequence = [];
    private DateTimeOffset _now;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    /// <returns>Whether anything was created.</returns>
    public async Task<bool> SeedAsync(DemoDataOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (await db.Customers.AnyAsync(cancellationToken))
        {
            return false;
        }

        _now = timeProvider.GetUtcNow();
        _timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;
        var bookedAt = At(-1, 10, 0);

        await AddStaffAsync(options.StaffPassword, cancellationToken);

        var lift1 = Bay("Lift 1");
        var lift2 = Bay("Lift 2");
        var lift3 = Bay("Lift 3");
        lift3.TakeOutOfService();
        Bay("Alignment bay");

        var anna = Customer("Anna", "Schmidt", "+49 30 1234 5601", -400);
        var jonas = Customer("Jonas", "Becker", "+49 30 1234 5602", -380);
        var mia = Customer("Mia", "Wagner", null, -300);
        var lukas = Customer("Lukas", "Hoffmann", "+49 30 1234 5604", -200);
        var sophie = Customer("Sophie", "Keller", "+49 30 1234 5605", -120);
        var elias = Customer("Elias", "Neumann", "+49 30 1234 5606", -90);
        var emma = Customer("Emma", "Richter", null, -60);
        var paul = Customer("Paul", "Wolf", "+49 30 1234 5608", -30);

        var golf = Vehicle(anna, "WVWZZZ1KZBW000101", "B-AS 1201", "Volkswagen", "Golf", 2019, 61_250);
        var bmw = Vehicle(jonas, "WBA8E1C50HK000202", "B-JB 4521", "BMW", "320d", 2021, 38_900);
        var corsa = Vehicle(mia, "W0L0XEP68J4000303", "B-MW 3110", "Opel", "Corsa", 2018, 97_400);
        var audi = Vehicle(lukas, "WAUZZZF45LA000404", "B-LH 7788", "Audi", "A4 Avant", 2020, 54_100);
        var octavia = Vehicle(sophie, "TMBJJ7NE8M0000505", "B-SK 2024", "Skoda", "Octavia", 2022, 21_800);
        var transit = Vehicle(elias, "WF0XXXTTGXHU00606", "B-EN 905", "Ford", "Transit", 2017, 188_300);
        var yaris = Vehicle(emma, "VNKKH3D3X0A000707", "B-ER 512", "Toyota", "Yaris Hybrid", 2023, 9_700);
        var mercedes = Vehicle(paul, "WDD2050041R000808", "B-PW 220", "Mercedes-Benz", "C 220 d", 2019, 112_500);

        // Today: the morning's first car is in the workshop, a customer did not come, the rest is planned.
        var annual = Book(lift1, golf, At(0, 8, 0), 2, "Annual service with oil change", bookedAt);
        var serviceOrder = Open(golf, "Annual service with oil change; warning light for brake pads", At(0, 7, 55));
        serviceOrder.AddLabour("Annual service according to manufacturer schedule", 2m, 95m);
        serviceOrder.AddPart("06L115562B", "Oil filter", 1m, 14.9m);
        serviceOrder.AddPart(null, "Engine oil 5W-30, litre", 5m, 12.5m);
        serviceOrder.Approve(At(0, 8, 20));
        serviceOrder.Start(At(0, 8, 30));
        annual.CheckIn(serviceOrder.Id, At(0, 7, 55));

        Book(lift1, bmw, At(0, 10, 30), 1.5, "Replace front brake pads", bookedAt);
        Book(lift1, corsa, At(0, 13, 0), 3.5, "Timing belt and water pump", bookedAt);
        var tyres = Book(lift2, audi, At(0, 9, 0), 0.75, "Seasonal tyre change", bookedAt);
        tyres.MarkNoShow(At(0, 9, 30));
        Book(lift2, octavia, At(0, 11, 0), 2, "Air conditioning service", bookedAt);
        var cancelled = Book(lift2, yaris, At(0, 15, 0), 1, "First inspection", bookedAt);
        cancelled.Cancel("Customer is travelling; will call to rebook", At(-1, 16, 0));

        // Tomorrow.
        Book(lift2, transit, At(1, 8, 0), 4, "Clutch replacement", bookedAt);
        Book(lift1, yaris, At(1, 14, 0), 1, "First inspection", bookedAt);

        // An estimate waiting for the customer's approval.
        var estimate = Open(audi, "Rattling noise from the rear axle over bumps", At(0, 9, 40));
        estimate.AddLabour("Diagnosis rear axle", 0.5m, 95m);
        estimate.AddPart("8W0513035AB", "Rear shock absorber", 2m, 129.9m);
        estimate.AddLabour("Replace rear shock absorbers", 1.8m, 95m);

        // Finished jobs, invoiced in this order: one overdue, one partly paid, one paid.
        Invoiced(
            corsa,
            "Exhaust rattles at idle",
            -20,
            order =>
            {
                order.AddLabour("Replace exhaust mounting rubbers", 0.8m, 95m);
                order.AddPart(null, "Exhaust mounting rubber", 3m, 6.4m);
            },
            (_, _) => { });
        Invoiced(
            bmw,
            "Inspection and new wiper blades",
            -10,
            order =>
            {
                order.AddLabour("Inspection", 1.2m, 95m);
                order.AddPart(null, "Wiper blade set", 1m, 34.9m);
            },
            (invoice, issuedAt) => invoice.RecordPayment(100m, PaymentMethod.Cash, issuedAt.AddMinutes(5)));
        Invoiced(
            mercedes,
            "Front brake discs and pads",
            -3,
            order =>
            {
                order.AddLabour("Replace front brake discs and pads", 1.5m, 95m);
                order.AddPart("A2054211012", "Brake disc, front", 2m, 89.5m);
                order.AddPart("A0004205802", "Brake pad set, front", 1m, 74.9m);
            },
            (invoice, issuedAt) => invoice.RecordPayment(invoice.GrossTotal, PaymentMethod.Card, issuedAt.AddMinutes(10)));

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task AddStaffAsync(string password, CancellationToken cancellationToken)
    {
        foreach (var (email, name, role) in new[]
        {
            ("advisor@autoflow.demo", "Sam Adler", UserRole.ServiceAdvisor),
            ("mechanic@autoflow.demo", "Max Brandt", UserRole.Mechanic),
        })
        {
            var address = EmailAddress.Create(email);
            if (!await db.Users.AnyAsync(u => u.Email == address, cancellationToken))
            {
                db.Users.Add(User.Create(email, name, role, passwordHasher.Hash(password), _now));
            }
        }
    }

    private Bay Bay(string name)
    {
        var bay = Domain.Bays.Bay.Create(name, _now.AddDays(-400));
        db.Bays.Add(bay);
        return bay;
    }

    private Customer Customer(string firstName, string lastName, string? phone, int daysAgo)
    {
        var customer = Domain.Customers.Customer.Create(
            firstName,
            lastName,
            $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}@example.com",
            phone,
            _now.AddDays(daysAgo));
        db.Customers.Add(customer);
        return customer;
    }

    private Vehicle Vehicle(Customer owner, string vin, string plate, string make, string model, int year, int mileageKm)
    {
        var vehicle = Domain.Vehicles.Vehicle.Register(owner.Id, vin, plate, make, model, year, mileageKm, owner.CreatedAt);
        db.Vehicles.Add(vehicle);
        return vehicle;
    }

    private Appointment Book(Bay bay, Vehicle vehicle, DateTimeOffset start, double hours, string purpose, DateTimeOffset bookedAt)
    {
        var appointment = Appointment.Book(
            bay.Id,
            vehicle.Id,
            vehicle.CustomerId,
            AppointmentSlot.StartingAt(start, TimeSpan.FromHours(hours)),
            purpose,
            bookedAt);
        db.Appointments.Add(appointment);
        return appointment;
    }

    private WorkOrder Open(Vehicle vehicle, string complaint, DateTimeOffset openedAt)
    {
        var workOrder = WorkOrder.Open(vehicle.Id, vehicle.CustomerId, complaint, vehicle.MileageKm, openedAt);
        db.WorkOrders.Add(workOrder);
        return workOrder;
    }

    private void Invoiced(
        Vehicle vehicle,
        string complaint,
        int dayOffset,
        Action<WorkOrder> addLines,
        Action<Invoice, DateTimeOffset> pay)
    {
        var workOrder = Open(vehicle, complaint, At(dayOffset, 8, 15));
        addLines(workOrder);
        workOrder.Approve(At(dayOffset, 9, 0));
        workOrder.Start(At(dayOffset, 10, 0));
        workOrder.Complete(At(dayOffset, 14, 30));

        var issuedAt = At(dayOffset, 15, 0);
        var customer = db.Customers.Local.Single(c => c.Id == vehicle.CustomerId);
        var invoice = Invoice.IssueFor(
            workOrder,
            NextSequence(issuedAt),
            customer.FullName,
            $"{vehicle.Make} {vehicle.Model} ({vehicle.ModelYear}), {vehicle.LicensePlate.Value}, VIN {vehicle.Vin.Value}",
            invoicing.VatRatePercent,
            invoicing.PaymentTermDays,
            issuedAt);
        workOrder.MarkInvoiced(issuedAt);
        pay(invoice, issuedAt);
        db.Invoices.Add(invoice);
    }

    private int NextSequence(DateTimeOffset issuedAt)
    {
        var year = InvoiceNumber.YearOf(issuedAt);
        var next = _nextInvoiceSequence.GetValueOrDefault(year, 1);
        _nextInvoiceSequence[year] = next + 1;
        return next;
    }

    /// <summary>A wall-clock time <paramref name="dayOffset"/> days from today in the demo's time zone, as UTC.</summary>
    private DateTimeOffset At(int dayOffset, int hour, int minute)
    {
        var today = TimeZoneInfo.ConvertTime(_now, _timeZone).Date;
        var local = today.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(local, _timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
