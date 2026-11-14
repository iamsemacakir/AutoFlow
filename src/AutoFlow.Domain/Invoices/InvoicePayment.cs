using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Invoices;

/// <summary>Money received for an invoice. Recorded once, never changed.</summary>
public sealed class InvoicePayment : Entity
{
    internal InvoicePayment(Guid id, decimal amount, PaymentMethod method, DateTimeOffset receivedAt)
        : base(id)
    {
        Amount = amount;
        Method = method;
        ReceivedAt = receivedAt;
    }

    /// <summary>Gross amount received, in whole cents.</summary>
    public decimal Amount { get; }

    public PaymentMethod Method { get; }

    public DateTimeOffset ReceivedAt { get; }
}
