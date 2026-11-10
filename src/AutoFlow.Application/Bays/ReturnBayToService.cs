using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Bays;

public sealed record ReturnBayToServiceCommand(Guid BayId);

/// <summary>
/// Makes a bay bookable again. A bay that is already in service breaks a
/// rule (422).
/// </summary>
public sealed class ReturnBayToServiceHandler(IBayRepository bays, IUnitOfWork unitOfWork)
{
    public async Task<Result<BayDetails>> HandleAsync(
        ReturnBayToServiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var bay = await bays.GetByIdAsync(command.BayId, cancellationToken);
        if (bay is null)
        {
            return Result.Failure<BayDetails>(BayErrors.NotFound(command.BayId));
        }

        bay.ReturnToService();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BayDetails.From(bay));
    }
}
