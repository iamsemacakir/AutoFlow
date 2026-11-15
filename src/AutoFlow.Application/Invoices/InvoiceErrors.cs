using AutoFlow.Application.Common;
using AutoFlow.Domain.Invoices;

namespace AutoFlow.Application.Invoices;

public static class InvoiceErrors
{
    public static Error NotFound(Guid invoiceId) =>
        Error.NotFound("invoice.not_found", $"Invoice '{invoiceId}' was not found.");

    /// <summary>The work order exists but has not been invoiced (yet).</summary>
    public static Error NotIssued(Guid workOrderId) =>
        Error.NotFound("invoice.not_issued", $"No invoice has been issued for work order '{workOrderId}'.");

    public static Error UnknownStatus(string status) =>
        Error.Validation(
            "invoice.unknown_status",
            $"'{status}' is not an invoice status; use one of: {string.Join(", ", Enum.GetNames<InvoiceStatus>())}.");

    public static Error UnknownPaymentMethod(string method) =>
        Error.Validation(
            "invoice.unknown_payment_method",
            $"'{method}' is not a payment method; use one of: {string.Join(", ", Enum.GetNames<PaymentMethod>())}.");
}
