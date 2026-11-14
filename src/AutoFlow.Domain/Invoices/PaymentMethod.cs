namespace AutoFlow.Domain.Invoices;

/// <summary>How a payment reached the workshop. Stored by name.</summary>
public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    BankTransfer = 2,
}
