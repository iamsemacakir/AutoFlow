using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Tests.Appointments;

public class AppointmentSlotTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday8 = Midnight.AddHours(8);

    private static AppointmentSlot Slot(int startHour, int startMinute, int endHour, int endMinute) =>
        AppointmentSlot.Create(
            Midnight.AddHours(startHour).AddMinutes(startMinute),
            Midnight.AddHours(endHour).AddMinutes(endMinute));

    [Fact]
    public void Create_keeps_start_end_and_duration()
    {
        var slot = AppointmentSlot.Create(Monday8, Monday8.AddMinutes(90));

        Assert.Equal(Monday8, slot.Start);
        Assert.Equal(Monday8.AddMinutes(90), slot.End);
        Assert.Equal(TimeSpan.FromMinutes(90), slot.Duration);
    }

    [Fact]
    public void Create_converts_to_utc_so_equal_moments_are_equal_slots()
    {
        var berlin = TimeSpan.FromHours(1);
        var local = AppointmentSlot.Create(
            new DateTimeOffset(2026, 11, 9, 9, 0, 0, berlin),
            new DateTimeOffset(2026, 11, 9, 10, 0, 0, berlin));

        Assert.Equal(TimeSpan.Zero, local.Start.Offset);
        Assert.Equal(TimeSpan.Zero, local.End.Offset);
        Assert.Equal(AppointmentSlot.Create(Monday8, Monday8.AddHours(1)), local);
    }

    [Fact]
    public void StartingAt_adds_the_duration()
    {
        var slot = AppointmentSlot.StartingAt(Monday8, TimeSpan.FromHours(2));

        Assert.Equal(Monday8.AddHours(2), slot.End);
    }

    [Fact]
    public void Create_rejects_an_end_at_or_before_the_start()
    {
        Assert.Throws<DomainException>(() => AppointmentSlot.Create(Monday8, Monday8));
        Assert.Throws<DomainException>(() => AppointmentSlot.Create(Monday8, Monday8.AddMinutes(-15)));
    }

    [Theory]
    [InlineData(5, 0, 0)]  // start off the grid
    [InlineData(0, 10, 0)] // end off the grid
    [InlineData(0, 0, 30)] // seconds
    public void Create_rejects_times_off_the_quarter_hour_grid(int startMinutes, int endExtraMinutes, int startSeconds)
    {
        var start = Monday8.AddMinutes(startMinutes).AddSeconds(startSeconds);

        Assert.Throws<DomainException>(() =>
            AppointmentSlot.Create(start, Monday8.AddHours(1).AddMinutes(endExtraMinutes)));
    }

    [Fact]
    public void Create_accepts_the_shortest_and_longest_slot()
    {
        Assert.Equal(AppointmentSlot.MinDuration, AppointmentSlot.StartingAt(Monday8, AppointmentSlot.MinDuration).Duration);
        Assert.Equal(AppointmentSlot.MaxDuration, AppointmentSlot.StartingAt(Monday8, AppointmentSlot.MaxDuration).Duration);
    }

    [Fact]
    public void Create_rejects_a_slot_longer_than_the_maximum()
    {
        Assert.Throws<DomainException>(() =>
            AppointmentSlot.StartingAt(Monday8, AppointmentSlot.MaxDuration + AppointmentSlot.MinDuration));
    }

    [Theory]
    [InlineData(9, 0, 10, 0, true)]    // same slot
    [InlineData(9, 30, 10, 30, true)]  // overlaps the end
    [InlineData(8, 30, 9, 15, true)]   // overlaps the start
    [InlineData(9, 15, 9, 45, true)]   // inside
    [InlineData(8, 0, 11, 0, true)]    // around
    [InlineData(10, 0, 11, 0, false)]  // back to back after
    [InlineData(8, 0, 9, 0, false)]    // back to back before
    [InlineData(13, 0, 14, 0, false)]  // apart
    public void Overlaps_treats_slots_as_half_open(int sh, int sm, int eh, int em, bool expected)
    {
        var nineToTen = Slot(9, 0, 10, 0);
        var other = Slot(sh, sm, eh, em);

        Assert.Equal(expected, nineToTen.Overlaps(other));
        Assert.Equal(expected, other.Overlaps(nineToTen));
    }

    [Fact]
    public void ToString_shows_the_utc_interval()
    {
        Assert.Equal("2026-11-09 08:00–2026-11-09 09:30 UTC", AppointmentSlot.Create(Monday8, Monday8.AddMinutes(90)).ToString());
    }
}
