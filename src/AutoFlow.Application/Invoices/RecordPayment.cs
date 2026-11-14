using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Invoices;

namespace AutoFlow.Application.Invoices;

/// <param name="InvoiceId">The invoice the money is for.</param>
/// <param name="Amount">Gross amount received, in whole cents, at most the open balance.</param>
/// <param name="Method">Name of a <see cref="PaymentMethod"/>: <c>Cash</c>, <c>Card</c> or <c>BankTransfer</c>.</param>
/// <param name="ReceivedAt">When the money arrived; now if omitted (a bank transfer is often entered later).</param>
public sealed record RecordPaymentCommand(
    Guid InvoiceId,
    decimal Amount,
    string Method,
    DateTimeOffset? ReceivedAt = null);

/// <summary>
/// Records money received for an invoice and returns the whole invoice with
/// its new balance and status. An unknown method is a validation error (400);
/// an amount that is not positive whole cents or exceeds the balance, and a
/// payment on a paid invoice, break a rule (422).
/// </summary>
public sealed class RecordPaymentHandler(
    IInvoiceRepository invoices,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<InvoiceDetails>> HandleAsync(
        RecordPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!EnumNames.TryParse<PaymentMethod>(command.Method, out var method))
        {
            return Result.Failure<InvoiceDetails>(InvoiceErrors.UnknownPaymentMethod(command.Method));
        }

        var invoice = await invoices.GetByIdAsync(command.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<InvoiceDetails>(InvoiceErrors.NotFound(command.InvoiceId));
        }

        var now = timeProvider.GetUtcNow();
        invoice.RecordPayment(command.Amount, method, command.ReceivedAt?.ToUniversalTime() ?? now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(InvoiceDetails.From(invoice, now));
    }
}
