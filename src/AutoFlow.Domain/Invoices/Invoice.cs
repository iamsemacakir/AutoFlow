using System.Globalization;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Domain.Invoices;

/// <summary>
/// The bill for one completed work order. An invoice is a document, not a
/// view: it copies the lines, the customer's name and the vehicle as they
/// were when it was issued and never changes them afterwards, whatever
/// happens to the work order, the customer or the VAT rate later. The only
/// thing that happens to an invoice is that it gets paid (ADR 0018).
/// </summary>
/// <remarks>
/// <code>
/// Issued ──RecordPayment──► PartiallyPaid ──RecordPayment──► Paid
///    └───────────RecordPayment (full amount)──────────────────▲
/// </code>
/// Amounts: line totals are net; VAT is calculated once on the net total and
/// rounded half away from zero to cents; payments are gross.
/// </remarks>
public sealed class Invoice : Entity
{
    public const int CustomerNameMaxLength = 210;
    public const int VehicleDescriptionMaxLength = 200;
    public const decimal MaxVatRatePercent = 100m;
    public const int VatRateDecimals = 2;
    public const int MaxPaymentTermDays = 365;

    private readonly List<InvoiceLine> _lines = [];
    private readonly List<InvoicePayment> _payments = [];

    private Invoice(
        Guid id,
        int year,
        int sequence,
        Guid workOrderId,
        Guid customerId,
        string customerName,
        string vehicleDescription,
        decimal vatRatePercent,
        DateTimeOffset issuedAt,
        DateOnly dueDate)
        : base(id)
    {
        Year = year;
        Sequence = sequence;
        WorkOrderId = workOrderId;
        CustomerId = customerId;
        CustomerName = customerName;
        VehicleDescription = vehicleDescription;
        VatRatePercent = vatRatePercent;
        IssuedAt = issuedAt;
        DueDate = dueDate;
        Status = InvoiceStatus.Issued;
    }

    /// <summary>Calendar year (UTC) the invoice is numbered in.</summary>
    public int Year { get; }

    /// <summary>Position within <see cref="Year"/>, starting at 1, without gaps.</summary>
    public int Sequence { get; }

    /// <summary>The printed invoice number, e.g. <c>2026-00042</c>.</summary>
    public string Number => InvoiceNumber.Format(Year, Sequence);

    public Guid WorkOrderId { get; }

    /// <summary>The customer who commissioned the work order.</summary>
    public Guid CustomerId { get; }

    /// <summary>The customer's name as printed on the invoice.</summary>
    public string CustomerName { get; }

    /// <summary>The vehicle as printed on the invoice (make, model, plate, VIN).</summary>
    public string VehicleDescription { get; }

    /// <summary>VAT rate in percent at the time of issue, e.g. <c>19</c>.</summary>
    public decimal VatRatePercent { get; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset IssuedAt { get; }

    /// <summary>Last day (UTC) on which payment is on time.</summary>
    public DateOnly DueDate { get; }

    /// <summary>When the payment that settled the invoice was received.</summary>
    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>Sum of all payments received so far (gross).</summary>
    public decimal AmountPaid { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => [.. _lines.OrderBy(line => line.Number)];

    /// <summary>The payments in the order they were received.</summary>
    public IReadOnlyList<InvoicePayment> Payments => [.. _payments.OrderBy(payment => payment.ReceivedAt)];

    public decimal NetTotal => _lines.Sum(line => line.Total);

    public decimal VatAmount =>
        Math.Round(NetTotal * VatRatePercent / 100m, WorkOrderLine.MoneyDecimals, MidpointRounding.AwayFromZero);

    public decimal GrossTotal => NetTotal + VatAmount;

    /// <summary>What is still owed: the gross total minus the payments.</summary>
    public decimal Balance => GrossTotal - AmountPaid;

    /// <summary>
    /// Issues the invoice for a completed work order. The caller marks the
    /// work order as invoiced in the same unit of work.
    /// </summary>
    /// <param name="workOrder">The completed work order whose lines are copied.</param>
    /// <param name="sequence">The next free number of the year of <paramref name="issuedAt"/>.</param>
    /// <param name="customerName">Name of the customer of the work order.</param>
    /// <param name="vehicleDescription">The vehicle as it should be printed.</param>
    /// <param name="vatRatePercent">VAT rate in percent, 0 to 100, at most two decimals.</param>
    /// <param name="paymentTermDays">Days after the issue date until payment is due.</param>
    /// <param name="issuedAt">The moment of issue.</param>
    public static Invoice IssueFor(
        WorkOrder workOrder,
        int sequence,
        string customerName,
        string vehicleDescription,
        decimal vatRatePercent,
        int paymentTermDays,
        DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(workOrder);

        if (workOrder.Status == WorkOrderStatus.Invoiced)
        {
            throw new DomainException("The work order has already been invoiced.");
        }

        if (workOrder.Status != WorkOrderStatus.Completed)
        {
            throw new DomainException("Only a completed work order can be invoiced.");
        }

        if (sequence < 1)
        {
            throw new DomainException("An invoice number starts at 1.");
        }

        if (vatRatePercent < 0m || vatRatePercent > MaxVatRatePercent)
        {
            throw new DomainException($"The VAT rate must be between 0 and {MaxVatRatePercent} percent.");
        }

        if (vatRatePercent != Math.Round(vatRatePercent, VatRateDecimals))
        {
            throw new DomainException($"The VAT rate must have at most {VatRateDecimals} decimal places.");
        }

        if (paymentTermDays is < 0 or > MaxPaymentTermDays)
        {
            throw new DomainException($"The payment term must be between 0 and {MaxPaymentTermDays} days.");
        }

        var invoice = new Invoice(
            Guid.NewGuid(),
            InvoiceNumber.YearOf(issuedAt),
            sequence,
            workOrder.Id,
            workOrder.CustomerId,
            Guard.RequiredText(customerName, "Customer name", CustomerNameMaxLength),
            Guard.RequiredText(vehicleDescription, "Vehicle description", VehicleDescriptionMaxLength),
            vatRatePercent,
            issuedAt,
            DateOnly.FromDateTime(issuedAt.UtcDateTime).AddDays(paymentTermDays));

        var number = 0;
        foreach (var line in workOrder.Lines)
        {
            invoice._lines.Add(InvoiceLine.CopyOf(line, ++number));
        }

        // Goodwill work is documented with an invoice over nothing; there is nothing to wait for.
        if (invoice.GrossTotal == 0m)
        {
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = issuedAt;
        }

        return invoice;
    }

    /// <summary>
    /// Records money received. Part payments are fine; paying more than the
    /// open balance is not, so an overpayment is noticed at the till.
    /// </summary>
    public InvoicePayment RecordPayment(decimal amount, PaymentMethod method, DateTimeOffset receivedAt)
    {
        if (Status == InvoiceStatus.Paid)
        {
            throw new DomainException("The invoice is already paid in full.");
        }

        if (amount <= 0m)
        {
            throw new DomainException("A payment must be greater than 0.");
        }

        if (amount != Math.Round(amount, WorkOrderLine.MoneyDecimals))
        {
            throw new DomainException("A payment must be in whole cents.");
        }

        if (amount > Balance)
        {
            throw new DomainException(string.Create(
                CultureInfo.InvariantCulture,
                $"A payment of {amount:0.00} exceeds the open balance of {Balance:0.00}."));
        }

        if (!Enum.IsDefined(method))
        {
            throw new DomainException("Unknown payment method.");
        }

        var payment = new InvoicePayment(Guid.NewGuid(), amount, method, receivedAt);
        _payments.Add(payment);
        AmountPaid += amount;

        if (Balance == 0m)
        {
            Status = InvoiceStatus.Paid;
            PaidAt = receivedAt;
        }
        else
        {
            Status = InvoiceStatus.PartiallyPaid;
        }

        return payment;
    }

    /// <summary>True if money is still owed after the due date has passed.</summary>
    public bool IsOverdue(DateOnly today) => Status != InvoiceStatus.Paid && today > DueDate;
}
