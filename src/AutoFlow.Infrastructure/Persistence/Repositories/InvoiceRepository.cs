using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class InvoiceRepository(AutoFlowDbContext db) : IInvoiceRepository
{
    // Lines and payments are owned by the invoice, so EF Core loads them with it.
    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<Invoice?> GetByWorkOrderIdAsync(Guid workOrderId, CancellationToken cancellationToken = default) =>
        db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.WorkOrderId == workOrderId, cancellationToken);

    public async Task<int> GetLastSequenceAsync(int year, CancellationToken cancellationToken = default) =>
        await db.Invoices
            .Where(i => i.Year == year)
            .MaxAsync(i => (int?)i.Sequence, cancellationToken) ?? 0;

    public async Task<PagedList<Invoice>> ListAsync(
        InvoiceStatus? status,
        Guid? customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Invoice> matching = db.Invoices.AsNoTracking();

        if (status is { } wantedStatus)
        {
            matching = matching.Where(i => i.Status == wantedStatus);
        }

        if (customerId is { } wantedCustomer)
        {
            matching = matching.Where(i => i.CustomerId == wantedCustomer);
        }

        var totalCount = await matching.CountAsync(cancellationToken);
        var items = await matching
            .OrderByDescending(i => i.Year)
            .ThenByDescending(i => i.Sequence)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<Invoice>(items, page, pageSize, totalCount);
    }

    public void Add(Invoice invoice) => db.Invoices.Add(invoice);
}
