using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Bays;

public sealed record GetBayQuery(Guid BayId);

/// <summary>Returns one bay.</summary>
public sealed class GetBayHandler(IBayRepository bays)
{
    public async Task<Result<BayDetails>> HandleAsync(
        GetBayQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var bay = await bays.GetByIdAsync(query.BayId, cancellationToken);

        return bay is null
            ? Result.Failure<BayDetails>(BayErrors.NotFound(query.BayId))
            : Result.Success(BayDetails.From(bay));
    }
}
