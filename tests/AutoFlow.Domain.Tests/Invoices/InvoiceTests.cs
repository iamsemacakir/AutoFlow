using AutoFlow.Domain.Common;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Domain.Tests.Invoices;

public class InvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 30, 0, TimeSpan.Zero);

    /// <summary>Labour 1.5 h × 95.00 and two discs at 48.50: 239.50 net.</summary>
    private static WorkOrder CompletedWorkOrder(Action<WorkOrder>? estimate = null)
    {
        var workOrder = WorkOrder.Open(Guid.NewGuid(), Guid.NewGuid(), "Grinding noise when braking", 48_950, Now.AddDays(-1));
        if (estimate is null)
        {
            workOrder.AddLabour("Replace front pads and discs", 1.5m, 95m);
            workOrder.AddPart("0986494", "Brake disc, front", 2m, 48.5m);
        }
        else
        {
            estimate(workOrder);
        }

        workOrder.Approve(Now.AddHours(-5));
        workOrder.Start(Now.AddHours(-4));
        workOrder.Complete(Now.AddHours(-1));
        return workOrder;
    }

    private static Invoice Issue(WorkOrder? workOrder = null, decimal vatRatePercent = 19m, int paymentTermDays = 14) =>
        Invoice.IssueFor(
            workOrder ?? CompletedWorkOrder(),
            42,
            "  Anna Schmidt ",
            "Volkswagen Golf (2019), M-AB 123",
            vatRatePercent,
            paymentTermDays,
            Now);

    [Fact]
    public void IssueFor_copies_the_work_order_and_numbers_the_invoice_in_the_year_of_issue()
    {
        var workOrder = CompletedWorkOrder();

        var invoice = Issue(workOrder);

        Assert.NotEqual(Guid.Empty, invoice.Id);
        Assert.Equal((2026, 42, "2026-00042"), (invoice.Year, invoice.Sequence, invoice.Number));
        Assert.Equal((workOrder.Id, workOrder.CustomerId), (invoice.WorkOrderId, invoice.CustomerId));
        Assert.Equal("Anna Schmidt", invoice.CustomerName);
        Assert.Equal("Volkswagen Golf (2019), M-AB 123", invoice.VehicleDescription);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(Now, invoice.IssuedAt);
        Assert.Equal(new DateOnly(2026, 11, 30), invoice.DueDate);
        Assert.Null(invoice.PaidAt);
        Assert.Empty(invoice.Payments);
        Assert.Collection(
            invoice.Lines,
            labour =>
            {
                Assert.Equal((1, WorkOrderLineKind.Labour, "Replace front pads and discs"), (labour.Number, labour.Kind, labour.Description));
                Assert.Equal((1.5m, 95m, 142.5m), (labour.Quantity, labour.UnitPrice, labour.Total));
                Assert.Null(labour.PartNumber);
            },
            part =>
            {
                Assert.Equal((2, WorkOrderLineKind.Part, "0986494"), (part.Number, part.Kind, part.PartNumber));
                Assert.Equal(97m, part.Total);
            });
    }

    [Fact]
    public void Totals_add_vat_once_on_the_net_total_rounded_to_cents()
    {
        var invoice = Issue();

        Assert.Equal(239.5m, invoice.NetTotal);
        Assert.Equal(45.51m, invoice.VatAmount); // 45.505 rounds half away from zero
        Assert.Equal(285.01m, invoice.GrossTotal);
        Assert.Equal(285.01m, invoice.Balance);
        Assert.Equal(0m, invoice.AmountPaid);
    }

    [Fact]
    public void Lines_are_renumbered_without_the_gaps_removed_lines_left_on_the_work_order()
    {
        var workOrder = CompletedWorkOrder(w =>
        {
            var dropped = w.AddLabour("Diagnosis", 0.5m, 95m);
            w.AddLabour("Replace wiper blades", 0.25m, 95m);
            w.AddPart(null, "Wiper blade set", 1m, 24.9m);
            w.RemoveLine(dropped.Id);
        });

        var invoice = Issue(workOrder);

        Assert.Equal([1, 2], invoice.Lines.Select(l => l.Number).ToArray());
        Assert.Equal([2, 3], workOrder.Lines.Select(l => l.Number).ToArray());
    }

    [Fact]
    public void Invoice_is_numbered_and_due_by_the_utc_calendar()
    {
        // 00:30 on 1 January in Berlin is still 31 December in UTC, the calendar invoices are numbered in.
        var newYearsEve = new DateTimeOffset(2027, 1, 1, 0, 30, 0, TimeSpan.FromHours(1));

        var invoice = Invoice.IssueFor(CompletedWorkOrder(), 7, "Anna Schmidt", "VW Golf", 19m, 0, newYearsEve);

        Assert.Equal("2026-00007", invoice.Number);
        Assert.Equal(new DateOnly(2026, 12, 31), invoice.DueDate);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Draft)]
    [InlineData(WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStatus.InProgress)]
    public void IssueFor_rejects_a_work_order_that_is_not_completed(WorkOrderStatus status)
    {
        var workOrder = WorkOrder.Open(Guid.NewGuid(), Guid.NewGuid(), "Oil change", 10_000, Now);
        workOrder.AddLabour("Oil change", 0.5m, 95m);
        if (status >= WorkOrderStatus.Approved)
        {
            workOrder.Approve(Now);
        }

        if (status >= WorkOrderStatus.InProgress)
        {
            workOrder.Start(Now);
        }

        var exception = Assert.Throws<DomainException>(() => Issue(workOrder));
        Assert.Contains("completed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IssueFor_rejects_a_work_order_that_was_already_invoiced()
    {
        var workOrder = CompletedWorkOrder();
        workOrder.MarkInvoiced(Now);

        var exception = Assert.Throws<DomainException>(() => Issue(workOrder));
        Assert.Contains("already been invoiced", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(19.125)]
    public void IssueFor_rejects_an_invalid_vat_rate(double rate)
    {
        Assert.Throws<DomainException>(() => Issue(vatRatePercent: (decimal)rate));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void IssueFor_rejects_a_payment_term_out_of_range(int days)
    {
        Assert.Throws<DomainException>(() => Issue(paymentTermDays: days));
    }

    [Fact]
    public void IssueFor_rejects_a_sequence_below_one_and_a_blank_customer_name()
    {
        var workOrder = CompletedWorkOrder();

        Assert.Throws<DomainException>(() => Invoice.IssueFor(workOrder, 0, "Anna Schmidt", "VW Golf", 19m, 14, Now));
        Assert.Throws<DomainException>(() => Invoice.IssueFor(workOrder, 1, "  ", "VW Golf", 19m, 14, Now));
    }

    [Fact]
    public void Invoice_without_vat_has_equal_net_and_gross_totals()
    {
        var invoice = Issue(vatRatePercent: 0m);

        Assert.Equal(0m, invoice.VatAmount);
        Assert.Equal(invoice.NetTotal, invoice.GrossTotal);
    }

    [Fact]
    public void Invoice_over_nothing_is_paid_when_it_is_issued()
    {
        var goodwill = CompletedWorkOrder(w => w.AddLabour("Goodwill: re-tighten wheel bolts", 0.25m, 0m));

        var invoice = Issue(goodwill);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal<DateTimeOffset?>(Now, invoice.PaidAt);
        Assert.Throws<DomainException>(() => invoice.RecordPayment(1m, PaymentMethod.Cash, Now));
    }

    [Fact]
    public void Part_payment_leaves_a_balance_and_the_rest_settles_the_invoice()
    {
        var invoice = Issue();

        var deposit = invoice.RecordPayment(100m, PaymentMethod.Cash, Now.AddDays(1));

        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal((100m, 185.01m), (invoice.AmountPaid, invoice.Balance));
        Assert.Null(invoice.PaidAt);
        Assert.Equal((100m, PaymentMethod.Cash), (deposit.Amount, deposit.Method));

        invoice.RecordPayment(185.01m, PaymentMethod.BankTransfer, Now.AddDays(9));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal((285.01m, 0m), (invoice.AmountPaid, invoice.Balance));
        Assert.Equal<DateTimeOffset?>(Now.AddDays(9), invoice.PaidAt);
        Assert.Equal([100m, 185.01m], invoice.Payments.Select(p => p.Amount).ToArray());
    }

    [Fact]
    public void Payment_in_full_settles_the_invoice_at_once()
    {
        var invoice = Issue();

        invoice.RecordPayment(invoice.GrossTotal, PaymentMethod.Card, Now);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal<DateTimeOffset?>(Now, invoice.PaidAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    [InlineData(285.02)]
    public void RecordPayment_rejects_amounts_that_are_not_positive_cents_within_the_balance(double amount)
    {
        var invoice = Issue();

        Assert.Throws<DomainException>(() => invoice.RecordPayment((decimal)amount, PaymentMethod.Cash, Now));
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public void RecordPayment_rejects_an_unknown_method_and_a_payment_on_a_paid_invoice()
    {
        var invoice = Issue();

        Assert.Throws<DomainException>(() => invoice.RecordPayment(10m, (PaymentMethod)99, Now));

        invoice.RecordPayment(invoice.Balance, PaymentMethod.Cash, Now);
        var exception = Assert.Throws<DomainException>(() => invoice.RecordPayment(0.01m, PaymentMethod.Cash, Now));
        Assert.Contains("already paid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoice_is_overdue_after_the_due_date_until_it_is_paid()
    {
        var invoice = Issue();

        Assert.False(invoice.IsOverdue(invoice.DueDate));
        Assert.True(invoice.IsOverdue(invoice.DueDate.AddDays(1)));

        invoice.RecordPayment(invoice.Balance, PaymentMethod.Cash, Now.AddDays(20));
        Assert.False(invoice.IsOverdue(invoice.DueDate.AddDays(30)));
    }
}
