using AutoFlow.Domain.Bays;

namespace AutoFlow.Application.Bays;

/// <summary>Read model of a bay returned by the bay use cases.</summary>
public sealed record BayDetails(Guid Id, string Name, bool IsInService, DateTimeOffset CreatedAt)
{
    public static BayDetails From(Bay bay)
    {
        ArgumentNullException.ThrowIfNull(bay);

        return new(bay.Id, bay.Name, bay.IsInService, bay.CreatedAt);
    }
}
