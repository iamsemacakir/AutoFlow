using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.WorkOrders;
using AutoFlow.Infrastructure.Persistence.Configurations;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Tests;

[Collection(PostgresDefinition.Name)]
public class InvoicePersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Issued_invoice_round_trips_with_its_lines_and_totals()
    {
        var workOrder = await SeedCompletedWorkOrderAsync();
        var year = TestData.UniqueInvoiceYear();
        var invoice = TestData.Invoice(workOrder, year, sequence: 42);

        await using (var db = postgres.CreateContext())
        {
            new InvoiceRepository(db).Add(invoice);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var loaded = await new InvoiceRepository(readDb).GetByIdAsync(invoice.Id);

        Assert.NotNull(loaded);
        Assert.Equal((year, 42, invoice.Number), (loaded.Year, loaded.Sequence, loaded.Number));
        Assert.Equal((workOrder.Id, workOrder.CustomerId), (loaded.WorkOrderId, loaded.CustomerId));
        Assert.Equal("Anna Schmidt", loaded.CustomerName);
        Assert.Equal("Volkswagen Golf (2019), M-AB 123", loaded.VehicleDescription);
        Assert.Equal(19m, loaded.VatRatePercent);
        Assert.Equal(InvoiceStatus.Issued, loaded.Status);
        Assert.Equal(invoice.IssuedAt, loaded.IssuedAt);
        Assert.Equal(new DateOnly(year, 6, 15), loaded.DueDate);
        Assert.Equal((239.5m, 45.51m, 285.01m), (loaded.NetTotal, loaded.VatAmount, loaded.GrossTotal));
        Assert.Collection(
            loaded.Lines,
            labour => Assert.Equal((1, WorkOrderLineKind.Labour, 142.5m), (labour.Number, labour.Kind, labour.Total)),
            part => Assert.Equal((2, WorkOrderLineKind.Part, "0986494", 97m), (part.Number, part.Kind, part.PartNumber, part.Total)));
        Assert.Empty(loaded.Payments);
    }

    [Fact]
    public async Task Payments_are_saved_on_the_tracked_invoice_and_the_status_is_stored_by_name()
    {
        var invoice = await SeedInvoiceAsync();
        var received = invoice.IssuedAt.AddDays(3);

        await using (var db = postgres.CreateContext())
        {
            var tracked = await new InvoiceRepository(db).GetByIdAsync(invoice.Id);
            tracked!.RecordPayment(100m, PaymentMethod.Cash, received);
            await ((IUnitOfWork)db).SaveChangesAsync();
        }

        await using (var db = postgres.CreateContext())
        {
            var tracked = await new InvoiceRepository(db).GetByIdAsync(invoice.Id);
            Assert.Equal((InvoiceStatus.PartiallyPaid, 100m, 185.01m), (tracked!.Status, tracked.AmountPaid, tracked.Balance));
            tracked.RecordPayment(185.01m, PaymentMethod.BankTransfer, received.AddDays(7));
            await ((IUnitOfWork)db).SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.Invoices.SingleAsync(i => i.Id == invoice.Id);
        var status = await readDb.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM invoices WHERE id = {invoice.Id}")
            .SingleAsync();
        var methods = await readDb.Database
            .SqlQuery<string>($"SELECT method AS \"Value\" FROM invoice_payments WHERE invoice_id = {invoice.Id} ORDER BY received_at")
            .ToListAsync();

        Assert.Equal("Paid", status);
        Assert.Equal(["Cash", "BankTransfer"], methods.ToArray());
        Assert.Equal((InvoiceStatus.Paid, 285.01m, 0m), (reloaded.Status, reloaded.AmountPaid, reloaded.Balance));
        Assert.Equal<DateTimeOffset?>(received.AddDays(7), reloaded.PaidAt);
        Assert.Equal([100m, 185.01m], reloaded.Payments.Select(p => p.Amount).ToArray());
    }

    [Fact]
    public async Task Two_payments_recorded_at_once_cannot_both_be_saved()
    {
        var invoice = await SeedInvoiceAsync();

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var firstCopy = await new InvoiceRepository(first).GetByIdAsync(invoice.Id);
        var secondCopy = await new InvoiceRepository(second).GetByIdAsync(invoice.Id);

        // Each fits the balance on its own; together they would overpay.
        firstCopy!.RecordPayment(200m, PaymentMethod.Card, invoice.IssuedAt.AddDays(1));
        secondCopy!.RecordPayment(200m, PaymentMethod.Cash, invoice.IssuedAt.AddDays(1));
        await ((IUnitOfWork)first).SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ((IUnitOfWork)second).SaveChangesAsync());

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.Invoices.SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(200m, Assert.Single(reloaded.Payments).Amount);
        Assert.Equal(200m, reloaded.AmountPaid);
    }

    [Fact]
    public async Task Second_invoice_for_the_same_work_order_violates_the_work_order_index()
    {
        var workOrder = await SeedCompletedWorkOrderAsync();
        var year = TestData.UniqueInvoiceYear();
        await using (var seedDb = postgres.CreateContext())
        {
            seedDb.Invoices.Add(TestData.Invoice(workOrder, year, sequence: 1));
            await seedDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        db.Invoices.Add(TestData.Invoice(workOrder, year, sequence: 2));

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => ((IUnitOfWork)db).SaveChangesAsync());
        Assert.Equal(InvoiceConfiguration.WorkOrderIndex, exception.ConstraintName);
    }

    [Fact]
    public async Task Invoice_number_taken_twice_violates_the_number_index()
    {
        var first = await SeedCompletedWorkOrderAsync();
        var second = await SeedCompletedWorkOrderAsync();
        var year = TestData.UniqueInvoiceYear();
        await using (var seedDb = postgres.CreateContext())
        {
            seedDb.Invoices.Add(TestData.Invoice(first, year, sequence: 7));
            await seedDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        db.Invoices.Add(TestData.Invoice(second, year, sequence: 7));

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => ((IUnitOfWork)db).SaveChangesAsync());
        Assert.Equal(InvoiceConfiguration.NumberIndex, exception.ConstraintName);
    }

    [Fact]
    public async Task GetLastSequenceAsync_returns_the_highest_number_of_the_year_or_zero()
    {
        var year = TestData.UniqueInvoiceYear();
        var otherYear = TestData.UniqueInvoiceYear();
        await SeedInvoiceAsync(year, sequence: 1);
        await SeedInvoiceAsync(year, sequence: 2);
        await SeedInvoiceAsync(otherYear, sequence: 9);

        await using var db = postgres.CreateContext();
        var repository = new InvoiceRepository(db);

        Assert.Equal(2, await repository.GetLastSequenceAsync(year));
        Assert.Equal(9, await repository.GetLastSequenceAsync(otherYear));
        Assert.Equal(0, await repository.GetLastSequenceAsync(TestData.UniqueInvoiceYear()));
    }

    [Fact]
    public async Task GetByWorkOrderIdAsync_finds_the_invoice_of_a_work_order_without_tracking_it()
    {
        var invoice = await SeedInvoiceAsync();
        var notInvoiced = await SeedCompletedWorkOrderAsync();

        await using var db = postgres.CreateContext();
        var repository = new InvoiceRepository(db);

        var found = await repository.GetByWorkOrderIdAsync(invoice.WorkOrderId);

        Assert.Equal(invoice.Id, found?.Id);
        Assert.Equal(2, found!.Lines.Count);
        Assert.Null(await repository.GetByWorkOrderIdAsync(notInvoiced.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ListAsync_filters_by_customer_and_status_and_pages_newest_number_first()
    {
        var year = TestData.UniqueInvoiceYear();
        var first = await SeedCompletedWorkOrderAsync();
        var customerId = first.CustomerId;
        var open1 = await SeedInvoiceAsync(year, 1, first);
        var paid = await SeedInvoiceAsync(year, 2, await SeedCompletedWorkOrderAsync(customerId));
        var open3 = await SeedInvoiceAsync(year, 3, await SeedCompletedWorkOrderAsync(customerId));
        await SeedInvoiceAsync(year, 4); // another customer
        await using (var payDb = postgres.CreateContext())
        {
            var tracked = await payDb.Invoices.SingleAsync(i => i.Id == paid.Id);
            tracked.RecordPayment(tracked.Balance, PaymentMethod.Card, tracked.IssuedAt);
            await payDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        var repository = new InvoiceRepository(db);

        var all = await repository.ListAsync(null, customerId, page: 1, pageSize: 2);
        var secondPage = await repository.ListAsync(null, customerId, page: 2, pageSize: 2);
        var open = await repository.ListAsync(InvoiceStatus.Issued, customerId, page: 1, pageSize: 10);
        var settled = await repository.ListAsync(InvoiceStatus.Paid, customerId, page: 1, pageSize: 10);

        Assert.Equal(3, all.TotalCount);
        Assert.Equal([open3.Id, paid.Id], all.Items.Select(i => i.Id).ToArray());
        Assert.Equal(open1.Id, Assert.Single(secondPage.Items).Id);
        Assert.Equal([open3.Id, open1.Id], open.Items.Select(i => i.Id).ToArray());
        Assert.Equal(paid.Id, Assert.Single(settled.Items).Id);
        Assert.Equal(2, all.Items[0].Lines.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private async Task<Invoice> SeedInvoiceAsync(int? year = null, int sequence = 1, WorkOrder? workOrder = null)
    {
        var invoice = TestData.Invoice(
            workOrder ?? await SeedCompletedWorkOrderAsync(),
            year ?? TestData.UniqueInvoiceYear(),
            sequence);
        await using var db = postgres.CreateContext();
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task<WorkOrder> SeedCompletedWorkOrderAsync(Guid? customerId = null)
    {
        await using var db = postgres.CreateContext();
        if (customerId is null)
        {
            var customer = TestData.Customer();
            db.Customers.Add(customer);
            customerId = customer.Id;
        }

        var vehicle = TestData.Vehicle(customerId.Value);
        var workOrder = TestData.CompletedWorkOrder(vehicle.Id, customerId.Value);
        db.Vehicles.Add(vehicle);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        return workOrder;
    }
}
