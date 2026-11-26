using AutoFlow.Application.Common;
using AutoFlow.Domain.Invoices;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Persistence contract for invoices. Invoices are never deleted, so there
/// is no <c>Remove</c>. Entities returned by <see cref="GetByIdAsync"/> are
/// tracked: a payment recorded on them is stored on the next
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The invoice issued for a work order, if any. The returned entity is a
    /// read-only snapshot and is not tracked.
    /// </summary>
    Task<Invoice?> GetByWorkOrderIdAsync(Guid workOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The highest sequence used in <paramref name="year"/>, or 0 if no
    /// invoice was issued in it yet. The next invoice takes this plus one;
    /// the database rejects the loser when two requests take the same number
    /// at the same moment, so numbers stay consecutive (ADR 0019).
    /// </summary>
    Task<int> GetLastSequenceAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of invoices, newest number first, matching
    /// <paramref name="filter"/>. Lines and payments come with each invoice.
    /// The returned entities are read-only snapshots and are not tracked.
    /// </summary>
    Task<PagedList<Invoice>> ListAsync(
        InvoiceListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    void Add(Invoice invoice);
}

/// <summary>Which invoices <see cref="IInvoiceRepository.ListAsync"/> returns; a null criterion matches every invoice.</summary>
/// <param name="Status">Only invoices in this status.</param>
/// <param name="CustomerId">Only invoices of this customer.</param>
/// <param name="Overdue">
/// <c>true</c>: only invoices not yet paid whose due date is before
/// <paramref name="Today"/>; <c>false</c>: only the others. The same rule as
/// <see cref="Invoice.IsOverdue"/>.
/// </param>
/// <param name="Today">The day <paramref name="Overdue"/> is judged on.</param>
public sealed record InvoiceListFilter(
    InvoiceStatus? Status = null,
    Guid? CustomerId = null,
    bool? Overdue = null,
    DateOnly Today = default);
