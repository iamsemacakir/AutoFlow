using System.Globalization;
using System.Text;
using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Invoices;

namespace AutoFlow.Application.Tests.Fakes;

/// <summary>Renders an invoice as one line of text, so tests can see what the use case handed over.</summary>
public sealed class StubInvoiceRenderer : IInvoiceDocumentRenderer
{
    public InvoiceDocument Render(InvoiceDetails invoice, InvoicingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(settings);

        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{settings.SellerName}|{invoice.Number}|{invoice.Status}|{invoice.Balance}|overdue={invoice.IsOverdue}");
        return new InvoiceDocument($"{invoice.Number}.txt", "text/plain", Encoding.UTF8.GetBytes(text));
    }
}
