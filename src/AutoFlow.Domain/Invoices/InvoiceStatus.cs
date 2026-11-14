namespace AutoFlow.Domain.Invoices;

/// <summary>Where an invoice stands with regard to payment. Stored by name.</summary>
public enum InvoiceStatus
{
    /// <summary>Sent to the customer; nothing received yet.</summary>
    Issued = 0,

    /// <summary>Part of the gross total has been received.</summary>
    PartiallyPaid = 1,

    /// <summary>The gross total has been received in full.</summary>
    Paid = 2,
}
