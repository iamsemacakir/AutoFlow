using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Bays;

public sealed record TakeBayOutOfServiceCommand(Guid BayId);

/// <summary>
/// Stops new bookings for a bay (lift under repair). Appointments already
/// booked stay where they are until the service advisor reschedules them.
/// A bay that is already out of service breaks a rule (422).
/// </summary>
public sealed class TakeBayOutOfServiceHandler(IBayRepository bays, IUnitOfWork unitOfWork)
{
    public async Task<Result<BayDetails>> HandleAsync(
        TakeBayOutOfServiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var bay = await bays.GetByIdAsync(command.BayId, cancellationToken);
        if (bay is null)
        {
            return Result.Failure<BayDetails>(BayErrors.NotFound(command.BayId));
        }

        bay.TakeOutOfService();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BayDetails.From(bay));
    }
}
