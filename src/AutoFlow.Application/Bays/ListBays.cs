using AutoFlow.Application.Abstractions;

namespace AutoFlow.Application.Bays;

public sealed record ListBaysQuery;

/// <summary>
/// Lists every bay, in and out of service, by name. A workshop has a handful
/// of bays, so the list is not paged.
/// </summary>
public sealed class ListBaysHandler(IBayRepository bays)
{
    public async Task<IReadOnlyList<BayDetails>> HandleAsync(
        ListBaysQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await bays.ListAsync(cancellationToken);

        return [.. all.Select(BayDetails.From)];
    }
}
