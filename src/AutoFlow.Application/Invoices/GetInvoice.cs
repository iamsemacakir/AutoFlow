using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.WorkOrders;

namespace AutoFlow.Application.Invoices;

public sealed record GetInvoiceQuery(Guid InvoiceId);

/// <summary>Returns one invoice with its lines and payments.</summary>
public sealed class GetInvoiceHandler(IInvoiceRepository invoices, TimeProvider timeProvider)
{
    public async Task<Result<InvoiceDetails>> HandleAsync(
        GetInvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.GetByIdAsync(query.InvoiceId, cancellationToken);

        return invoice is null
            ? Result.Failure<InvoiceDetails>(InvoiceErrors.NotFound(query.InvoiceId))
            : Result.Success(InvoiceDetails.From(invoice, timeProvider.GetUtcNow()));
    }
}

public sealed record GetWorkOrderInvoiceQuery(Guid WorkOrderId);

/// <summary>
/// Returns the invoice issued for a work order. An unknown work order and a
/// work order without an invoice are both not found, with different codes
/// (<c>work_order.not_found</c>, <c>invoice.not_issued</c>).
/// </summary>
public sealed class GetWorkOrderInvoiceHandler(
    IWorkOrderRepository workOrders,
    IInvoiceRepository invoices,
    TimeProvider timeProvider)
{
    public async Task<Result<InvoiceDetails>> HandleAsync(
        GetWorkOrderInvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.GetByWorkOrderIdAsync(query.WorkOrderId, cancellationToken);
        if (invoice is not null)
        {
            return Result.Success(InvoiceDetails.From(invoice, timeProvider.GetUtcNow()));
        }

        return await workOrders.GetByIdAsync(query.WorkOrderId, cancellationToken) is null
            ? Result.Failure<InvoiceDetails>(WorkOrderErrors.NotFound(query.WorkOrderId))
            : Result.Failure<InvoiceDetails>(InvoiceErrors.NotIssued(query.WorkOrderId));
    }
}
