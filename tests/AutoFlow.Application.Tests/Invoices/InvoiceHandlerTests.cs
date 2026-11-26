using System.Globalization;
using System.Text;
using AutoFlow.Application.Common;
using AutoFlow.Application.Invoices;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.Tests.Invoices;

public class InvoiceHandlerTests
{
    private static readonly DateTimeOffset Now = FixedTimeProvider.Default;
    private static readonly string[] PaymentMethods = ["Cash", "BankTransfer", "Card"];

    private readonly InMemoryStore _store = new();
    private readonly Customer _customer;
    private readonly Vehicle _vehicle;

    public InvoiceHandlerTests()
    {
        _customer = _store.Seed(TestData.Customer());
        _vehicle = _store.Seed(TestData.Vehicle(_customer.Id));
    }

    private IssueInvoiceHandler Issue(InvoicingSettings? settings = null) =>
        new(_store, _store, _store, _store, _store, new FixedTimeProvider(), settings ?? InvoicingSettings.Default);

    private RecordPaymentHandler Pay(DateTimeOffset? now = null) =>
        new(_store, _store, new FixedTimeProvider(now ?? Now));

    private WorkOrder SeedCompletedWorkOrder() => _store.Seed(TestData.CompletedWorkOrder(_vehicle));

    private Invoice SeedInvoice(int sequence = 1, DateTimeOffset? issuedAt = null) =>
        _store.Seed(TestData.Invoice(SeedCompletedWorkOrder(), sequence, issuedAt));

    [Fact]
    public async Task Issue_bills_the_work_order_with_the_first_number_of_the_year_and_marks_it_invoiced()
    {
        var workOrder = SeedCompletedWorkOrder();

        var result = await Issue().HandleAsync(new IssueInvoiceCommand(workOrder.Id));

        Assert.True(result.IsSuccess);
        var invoice = result.Value;
        Assert.Equal("2026-00001", invoice.Number);
        Assert.Equal((workOrder.Id, _customer.Id), (invoice.WorkOrderId, invoice.CustomerId));
        Assert.Equal("Anna Schmidt", invoice.CustomerName);
        Assert.Equal("Volkswagen Golf (2019), M-AB 123, VIN WVWZZZ1JZXW000001", invoice.VehicleDescription);
        Assert.Equal("Issued", invoice.Status);
        Assert.Equal(Now, invoice.IssuedAt);
        Assert.Equal(new DateOnly(2026, 11, 3), invoice.DueDate);
        Assert.False(invoice.IsOverdue);
        Assert.Equal((19m, 239.5m, 45.51m, 285.01m), (invoice.VatRatePercent, invoice.NetTotal, invoice.VatAmount, invoice.GrossTotal));
        Assert.Equal((0m, 285.01m), (invoice.AmountPaid, invoice.Balance));
        Assert.Equal(["Labour", "Part"], invoice.Lines.Select(l => l.Kind).ToArray());
        Assert.Empty(invoice.Payments);

        Assert.Equal(WorkOrderStatus.Invoiced, workOrder.Status);
        Assert.Equal<DateTimeOffset?>(Now, workOrder.InvoicedAt);
        Assert.Equal(invoice.Id, Assert.Single(_store.Invoices).Id);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Issue_takes_the_number_after_the_last_one_of_the_year()
    {
        SeedInvoice(sequence: 41);
        SeedInvoice(sequence: 7, issuedAt: Now.AddYears(-1));
        var workOrder = SeedCompletedWorkOrder();

        var result = await Issue().HandleAsync(new IssueInvoiceCommand(workOrder.Id));

        Assert.Equal("2026-00042", result.Value.Number);
    }

    [Fact]
    public async Task Issue_uses_the_configured_vat_rate_and_payment_term()
    {
        var workOrder = SeedCompletedWorkOrder();
        var settings = new InvoicingSettings { VatRatePercent = 7m, PaymentTermDays = 30 };

        var result = await Issue(settings).HandleAsync(new IssueInvoiceCommand(workOrder.Id));

        Assert.Equal((7m, 16.77m, 256.27m), (result.Value.VatRatePercent, result.Value.VatAmount, result.Value.GrossTotal));
        Assert.Equal(new DateOnly(2026, 11, 19), result.Value.DueDate);
    }

    [Fact]
    public async Task Issue_for_an_unknown_work_order_returns_not_found()
    {
        var result = await Issue().HandleAsync(new IssueInvoiceCommand(Guid.NewGuid()));

        Assert.Equal("work_order.not_found", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Issue_for_a_work_order_in_progress_throws_and_saves_nothing()
    {
        var workOrder = _store.Seed(TestData.WorkOrder(_vehicle));
        workOrder.AddLabour("Oil change", 0.5m, 95m);
        workOrder.Approve(Now);
        workOrder.Start(Now);

        await Assert.ThrowsAsync<DomainException>(() => Issue().HandleAsync(new IssueInvoiceCommand(workOrder.Id)));

        Assert.Equal(WorkOrderStatus.InProgress, workOrder.Status);
        Assert.Empty(_store.Invoices);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Issue_twice_for_the_same_work_order_throws_the_second_time()
    {
        var workOrder = SeedCompletedWorkOrder();
        await Issue().HandleAsync(new IssueInvoiceCommand(workOrder.Id));

        await Assert.ThrowsAsync<DomainException>(() => Issue().HandleAsync(new IssueInvoiceCommand(workOrder.Id)));

        Assert.Single(_store.Invoices);
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("banktransfer")]
    [InlineData(" Card ")]
    public async Task Payment_in_part_leaves_a_balance(string method)
    {
        var invoice = SeedInvoice();

        var result = await Pay().HandleAsync(new RecordPaymentCommand(invoice.Id, 100m, method));

        Assert.True(result.IsSuccess);
        Assert.Equal("PartiallyPaid", result.Value.Status);
        Assert.Equal((100m, 185.01m), (result.Value.AmountPaid, result.Value.Balance));
        var payment = Assert.Single(result.Value.Payments);
        Assert.Equal((100m, Now), (payment.Amount, payment.ReceivedAt));
        Assert.Contains(payment.Method, PaymentMethods);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Payment_of_the_balance_settles_the_invoice_at_the_given_time()
    {
        var invoice = SeedInvoice();
        var received = new DateTimeOffset(2026, 10, 19, 16, 0, 0, TimeSpan.FromHours(2));

        var result = await Pay().HandleAsync(new RecordPaymentCommand(invoice.Id, 285.01m, "BankTransfer", received));

        Assert.Equal("Paid", result.Value.Status);
        Assert.Equal(0m, result.Value.Balance);
        Assert.Equal<DateTimeOffset?>(received, result.Value.PaidAt);
        Assert.Equal(TimeSpan.Zero, result.Value.PaidAt!.Value.Offset);
    }

    [Theory]
    [InlineData("Cheque")]
    [InlineData("1")]
    [InlineData("Cash, Card")]
    [InlineData("")]
    public async Task Payment_with_an_unknown_method_is_a_validation_error(string method)
    {
        var invoice = SeedInvoice();

        var result = await Pay().HandleAsync(new RecordPaymentCommand(invoice.Id, 10m, method));

        Assert.Equal("invoice.unknown_payment_method", result.Error.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Empty(invoice.Payments);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Payment_for_an_unknown_invoice_returns_not_found()
    {
        var result = await Pay().HandleAsync(new RecordPaymentCommand(Guid.NewGuid(), 10m, "Cash"));

        Assert.Equal("invoice.not_found", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task Payment_above_the_balance_throws_and_saves_nothing()
    {
        var invoice = SeedInvoice();

        await Assert.ThrowsAsync<DomainException>(
            () => Pay().HandleAsync(new RecordPaymentCommand(invoice.Id, 285.02m, "Cash")));

        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Get_returns_the_invoice_and_tells_when_it_is_overdue()
    {
        var invoice = SeedInvoice();
        var onDueDate = new GetInvoiceHandler(_store, new FixedTimeProvider(Now.AddDays(14)));
        var dayAfter = new GetInvoiceHandler(_store, new FixedTimeProvider(Now.AddDays(15)));

        var due = await onDueDate.HandleAsync(new GetInvoiceQuery(invoice.Id));
        var overdue = await dayAfter.HandleAsync(new GetInvoiceQuery(invoice.Id));
        var missing = await dayAfter.HandleAsync(new GetInvoiceQuery(Guid.NewGuid()));

        Assert.Equal(invoice.Number, due.Value.Number);
        Assert.False(due.Value.IsOverdue);
        Assert.True(overdue.Value.IsOverdue);
        Assert.Equal("invoice.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Get_for_a_work_order_distinguishes_no_invoice_from_no_work_order()
    {
        var invoice = SeedInvoice();
        var notInvoiced = SeedCompletedWorkOrder();
        var handler = new GetWorkOrderInvoiceHandler(_store, _store, new FixedTimeProvider());

        var found = await handler.HandleAsync(new GetWorkOrderInvoiceQuery(invoice.WorkOrderId));
        var notIssued = await handler.HandleAsync(new GetWorkOrderInvoiceQuery(notInvoiced.Id));
        var unknown = await handler.HandleAsync(new GetWorkOrderInvoiceQuery(Guid.NewGuid()));

        Assert.Equal(invoice.Id, found.Value.Id);
        Assert.Equal(("invoice.not_issued", ErrorKind.NotFound), (notIssued.Error.Code, notIssued.Error.Kind));
        Assert.Equal("work_order.not_found", unknown.Error.Code);
    }

    [Fact]
    public async Task List_filters_by_status_and_customer_newest_number_first()
    {
        var first = SeedInvoice(sequence: 1);
        var second = SeedInvoice(sequence: 2);
        second.RecordPayment(second.Balance, PaymentMethod.Card, Now);
        var third = SeedInvoice(sequence: 3);
        var otherCustomer = _store.Seed(TestData.Customer("Bernd", "Becker"));
        var otherVehicle = _store.Seed(TestData.Vehicle(otherCustomer.Id, vin: "WVWZZZ1JZXW000002"));
        _store.Seed(TestData.Invoice(_store.Seed(TestData.CompletedWorkOrder(otherVehicle)), sequence: 4));
        var handler = new ListInvoicesHandler(_store, new FixedTimeProvider());

        var all = await handler.HandleAsync(new ListInvoicesQuery());
        var mine = await handler.HandleAsync(new ListInvoicesQuery(CustomerId: _customer.Id, PageSize: 2));
        var open = await handler.HandleAsync(new ListInvoicesQuery(Status: "issued", CustomerId: _customer.Id));
        var paid = await handler.HandleAsync(new ListInvoicesQuery(Status: "Paid"));

        Assert.Equal(4, all.Value.TotalCount);
        Assert.Equal((3, 2), (mine.Value.TotalCount, mine.Value.TotalPages));
        Assert.Equal([third.Id, second.Id], mine.Value.Items.Select(i => i.Id).ToArray());
        Assert.Equal([third.Id, first.Id], open.Value.Items.Select(i => i.Id).ToArray());
        Assert.Equal(second.Id, Assert.Single(paid.Value.Items).Id);
    }

    [Fact]
    public async Task List_by_overdue_judges_unpaid_invoices_by_todays_date()
    {
        // Issued at Now with a 14-day term: due on Now + 14 days, overdue from the day after.
        var open = SeedInvoice(sequence: 1);
        var partlyPaid = SeedInvoice(sequence: 2);
        partlyPaid.RecordPayment(100m, PaymentMethod.Cash, Now);
        var paid = SeedInvoice(sequence: 3);
        paid.RecordPayment(paid.Balance, PaymentMethod.Card, Now);
        var recent = SeedInvoice(sequence: 4, issuedAt: Now.AddDays(10));
        var onDueDate = new ListInvoicesHandler(_store, new FixedTimeProvider(Now.AddDays(14)));
        var dayAfter = new ListInvoicesHandler(_store, new FixedTimeProvider(Now.AddDays(15)));

        var noneYet = await onDueDate.HandleAsync(new ListInvoicesQuery(Overdue: true));
        var overdue = await dayAfter.HandleAsync(new ListInvoicesQuery(Overdue: true));
        var notOverdue = await dayAfter.HandleAsync(new ListInvoicesQuery(Overdue: false));
        var overduePartlyPaid = await dayAfter.HandleAsync(new ListInvoicesQuery(Status: "PartiallyPaid", Overdue: true));

        Assert.Equal(0, noneYet.Value.TotalCount);
        Assert.Equal([partlyPaid.Id, open.Id], overdue.Value.Items.Select(i => i.Id).ToArray());
        Assert.All(overdue.Value.Items, i => Assert.True(i.IsOverdue));
        Assert.Equal([recent.Id, paid.Id], notOverdue.Value.Items.Select(i => i.Id).ToArray());
        Assert.Equal(partlyPaid.Id, Assert.Single(overduePartlyPaid.Value.Items).Id);
    }

    [Fact]
    public async Task List_with_an_unknown_status_or_invalid_paging_is_a_validation_error()
    {
        var handler = new ListInvoicesHandler(_store, new FixedTimeProvider());

        var unknownStatus = await handler.HandleAsync(new ListInvoicesQuery(Status: "Overdue"));
        var badPage = await handler.HandleAsync(new ListInvoicesQuery(Page: 0));

        Assert.Equal(("invoice.unknown_status", ErrorKind.Validation), (unknownStatus.Error.Code, unknownStatus.Error.Kind));
        Assert.Equal("paging.invalid_page", badPage.Error.Code);
    }

    [Fact]
    public async Task Export_renders_the_invoice_as_it_stands_now_with_the_workshops_settings()
    {
        var invoice = SeedInvoice(sequence: 42);
        invoice.RecordPayment(100m, PaymentMethod.Cash, Now);
        var settings = new InvoicingSettings { SellerName = "Garage Becker" };
        var handler = new ExportInvoiceHandler(_store, new StubInvoiceRenderer(), settings, new FixedTimeProvider(Now.AddDays(30)));

        var result = await handler.HandleAsync(new ExportInvoiceQuery(invoice.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(("2026-00042.txt", "text/plain"), (result.Value.FileName, result.Value.ContentType));
        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"Garage Becker|2026-00042|PartiallyPaid|{185.01m}|overdue=True"),
            Encoding.UTF8.GetString(result.Value.Content.Span));
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Export_of_an_unknown_invoice_returns_not_found()
    {
        var handler = new ExportInvoiceHandler(_store, new StubInvoiceRenderer(), InvoicingSettings.Default, new FixedTimeProvider());

        var result = await handler.HandleAsync(new ExportInvoiceQuery(Guid.NewGuid()));

        Assert.Equal("invoice.not_found", result.Error.Code);
    }

    [Theory]
    [InlineData(-1, 14)]
    [InlineData(100.5, 14)]
    [InlineData(19.999, 14)]
    [InlineData(19, -1)]
    [InlineData(19, 366)]
    public void Settings_out_of_range_are_rejected_before_the_first_invoice(double vatRatePercent, int paymentTermDays)
    {
        var settings = new InvoicingSettings { VatRatePercent = (decimal)vatRatePercent, PaymentTermDays = paymentTermDays };

        Assert.Throws<InvalidOperationException>(() => settings.Validated());
        Assert.Same(InvoicingSettings.Default, InvoicingSettings.Default.Validated());
    }
}
