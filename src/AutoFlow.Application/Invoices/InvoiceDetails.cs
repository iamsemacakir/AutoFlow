using AutoFlow.Domain.Invoices;

namespace AutoFlow.Application.Invoices;

/// <summary>
/// Read model of an invoice returned by the invoice use cases. Amounts are
/// in the workshop's currency: line totals and <see cref="NetTotal"/> are
/// net, <see cref="GrossTotal"/>, <see cref="AmountPaid"/> and
/// <see cref="Balance"/> include VAT. <see cref="IsOverdue"/> is true while
/// money is owed after <see cref="DueDate"/> (judged at the time of the request).
/// </summary>
public sealed record InvoiceDetails(
    Guid Id,
    string Number,
    Guid WorkOrderId,
    Guid CustomerId,
    string CustomerName,
    string VehicleDescription,
    string Status,
    DateTimeOffset IssuedAt,
    DateOnly DueDate,
    bool IsOverdue,
    decimal VatRatePercent,
    decimal NetTotal,
    decimal VatAmount,
    decimal GrossTotal,
    decimal AmountPaid,
    decimal Balance,
    DateTimeOffset? PaidAt,
    IReadOnlyList<InvoiceLineDetails> Lines,
    IReadOnlyList<InvoicePaymentDetails> Payments)
{
    /// <param name="invoice">The invoice to describe.</param>
    /// <param name="now">The current time; decides <see cref="IsOverdue"/> by its UTC date.</param>
    public static InvoiceDetails From(Invoice invoice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new(
            invoice.Id,
            invoice.Number,
            invoice.WorkOrderId,
            invoice.CustomerId,
            invoice.CustomerName,
            invoice.VehicleDescription,
            invoice.Status.ToString(),
            invoice.IssuedAt,
            invoice.DueDate,
            invoice.IsOverdue(DateOnly.FromDateTime(now.UtcDateTime)),
            invoice.VatRatePercent,
            invoice.NetTotal,
            invoice.VatAmount,
            invoice.GrossTotal,
            invoice.AmountPaid,
            invoice.Balance,
            invoice.PaidAt,
            [.. invoice.Lines.Select(InvoiceLineDetails.From)],
            [.. invoice.Payments.Select(InvoicePaymentDetails.From)]);
    }
}

/// <summary>Read model of one invoice line; the kind is its name (<c>"Labour"</c>, <c>"Part"</c>).</summary>
public sealed record InvoiceLineDetails(
    int Number,
    string Kind,
    string Description,
    string? PartNumber,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total)
{
    public static InvoiceLineDetails From(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new(
            line.Number,
            line.Kind.ToString(),
            line.Description,
            line.PartNumber,
            line.Quantity,
            line.UnitPrice,
            line.Total);
    }
}

/// <summary>Read model of a payment; the method is its name (<c>"BankTransfer"</c>).</summary>
public sealed record InvoicePaymentDetails(Guid Id, decimal Amount, string Method, DateTimeOffset ReceivedAt)
{
    public static InvoicePaymentDetails From(InvoicePayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new(payment.Id, payment.Amount, payment.Method.ToString(), payment.ReceivedAt);
    }
}
