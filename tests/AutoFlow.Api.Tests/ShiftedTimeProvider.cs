namespace AutoFlow.Api.Tests;

/// <summary>
/// The real clock moved by an adjustable amount, for tests that need time to
/// pass between two requests (an appointment's slot beginning, an invoice
/// becoming overdue) without waiting for it.
/// </summary>
public sealed class ShiftedTimeProvider : TimeProvider
{
    private long _shiftTicks;

    public TimeSpan Shift
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _shiftTicks));
        set => Interlocked.Exchange(ref _shiftTicks, value.Ticks);
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Shift;
}
