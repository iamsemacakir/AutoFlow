using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Invoices;

namespace AutoFlow.Application.Invoices;

/// <param name="Status">Only invoices in this status (<c>Issued</c>, <c>PartiallyPaid</c>, <c>Paid</c>); all if omitted.</param>
/// <param name="CustomerId">Only invoices of this customer; all if omitted.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Invoices per page.</param>
public sealed record ListInvoicesQuery(
    string? Status = null,
    Guid? CustomerId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// Lists invoices page by page, newest number first. An unknown customer
/// simply has no invoices; an unknown status is a validation error (400).
/// </summary>
public sealed class ListInvoicesHandler(IInvoiceRepository invoices, TimeProvider timeProvider)
{
    public async Task<Result<PagedList<InvoiceDetails>>> HandleAsync(
        ListInvoicesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (Paging.Validate(query.Page, query.PageSize) is { } pagingError)
        {
            return Result.Failure<PagedList<InvoiceDetails>>(pagingError);
        }

        InvoiceStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!EnumNames.TryParse<InvoiceStatus>(query.Status, out var parsed))
            {
                return Result.Failure<PagedList<InvoiceDetails>>(InvoiceErrors.UnknownStatus(query.Status));
            }

            status = parsed;
        }

        var page = await invoices.ListAsync(status, query.CustomerId, query.Page, query.PageSize, cancellationToken);
        var now = timeProvider.GetUtcNow();

        return Result.Success(page.Map(invoice => InvoiceDetails.From(invoice, now)));
    }
}
