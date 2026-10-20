namespace AutoFlow.Application.Tests.Fakes;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public static readonly DateTimeOffset Default = new(2026, 10, 20, 8, 30, 0, TimeSpan.Zero);

    public FixedTimeProvider()
        : this(Default)
    {
    }

    public override DateTimeOffset GetUtcNow() => now;
}
