using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Bays;

namespace AutoFlow.Application.Bays;

public sealed record CreateBayCommand(string Name);

/// <summary>
/// Adds a bay to the workshop's capacity. It starts in service; its name must
/// not be in use by another bay, in or out of service.
/// </summary>
public sealed class CreateBayHandler(
    IBayRepository bays,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<BayDetails>> HandleAsync(
        CreateBayCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The domain validates and trims the name before it is compared.
        var bay = Bay.Create(command.Name, timeProvider.GetUtcNow());

        if (await bays.ExistsWithNameAsync(bay.Name, cancellationToken))
        {
            return Result.Failure<BayDetails>(BayErrors.DuplicateName(bay.Name));
        }

        bays.Add(bay);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BayDetails.From(bay));
    }
}
