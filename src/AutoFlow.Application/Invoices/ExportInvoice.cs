using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Invoices;

public sealed record ExportInvoiceQuery(Guid InvoiceId);

/// <summary>
/// Renders an invoice as a document for the customer, with the payments
/// received so far and the balance as of now. The invoice's own content
/// never changes, so exporting it again later prints the same positions.
/// </summary>
public sealed class ExportInvoiceHandler(
    IInvoiceRepository invoices,
    IInvoiceDocumentRenderer renderer,
    InvoicingSettings settings,
    TimeProvider timeProvider)
{
    public async Task<Result<InvoiceDocument>> HandleAsync(
        ExportInvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.GetByIdAsync(query.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<InvoiceDocument>(InvoiceErrors.NotFound(query.InvoiceId));
        }

        var details = InvoiceDetails.From(invoice, timeProvider.GetUtcNow());

        return Result.Success(renderer.Render(details, settings));
    }
}
