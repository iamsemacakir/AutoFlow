using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Bays;

public sealed record RenameBayCommand(Guid BayId, string Name);

/// <summary>
/// Renames a bay. Keeping its current name is allowed; taking the name of
/// another bay is a conflict.
/// </summary>
public sealed class RenameBayHandler(IBayRepository bays, IUnitOfWork unitOfWork)
{
    public async Task<Result<BayDetails>> HandleAsync(
        RenameBayCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var bay = await bays.GetByIdAsync(command.BayId, cancellationToken);
        if (bay is null)
        {
            return Result.Failure<BayDetails>(BayErrors.NotFound(command.BayId));
        }

        // A blank or overlong name is not looked up; Rename rejects it below.
        var name = Trimmed(command.Name);
        if (name.Length > 0
            && !string.Equals(name, bay.Name, StringComparison.Ordinal)
            && await bays.ExistsWithNameAsync(name, cancellationToken))
        {
            return Result.Failure<BayDetails>(BayErrors.DuplicateName(name));
        }

        bay.Rename(command.Name);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BayDetails.From(bay));
    }

    private static string Trimmed(string? name) => name?.Trim() ?? string.Empty;
}
