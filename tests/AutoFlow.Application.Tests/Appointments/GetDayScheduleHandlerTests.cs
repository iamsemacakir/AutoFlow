using AutoFlow.Application.Appointments;
using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Tests.Appointments;

public class GetDayScheduleHandlerTests
{
    /// <summary>The day of <see cref="TestData.Slot"/>.</summary>
    private static readonly DateOnly Day = DateOnly.FromDateTime(FixedTimeProvider.Default.Date.AddDays(1));

    private readonly InMemoryStore _store = new();
    private readonly GetDayScheduleHandler _handler;
    private readonly Vehicle _vehicle;

    public GetDayScheduleHandlerTests()
    {
        _handler = new GetDayScheduleHandler(_store, _store);
        var customer = _store.Seed(TestData.Customer());
        _vehicle = _store.Seed(TestData.Vehicle(customer.Id));
    }

    [Fact]
    public async Task Schedule_lists_every_bay_by_name_with_its_appointments_of_the_day_in_start_order()
    {
        var lift2 = _store.Seed(TestData.Bay("Lift 2"));
        var lift1 = _store.Seed(TestData.Bay("Lift 1"));
        var late = _store.Seed(TestData.Appointment(lift1, _vehicle, TestData.Slot(13, 2)));
        var early = _store.Seed(TestData.Appointment(lift1, _vehicle, TestData.Slot(9, 1)));
        var inLift2 = _store.Seed(TestData.Appointment(lift2, _vehicle, TestData.Slot(11, 1)));
        _store.Seed(TestData.Appointment(lift1, _vehicle, TestData.Slot(24 + 9, 1))); // next day

        var result = await _handler.HandleAsync(new GetDayScheduleQuery(Day, GetDayScheduleQuery.Utc));

        Assert.True(result.IsSuccess);
        var schedule = result.Value;
        Assert.Equal(Day, schedule.Date);
        Assert.Equal("UTC", schedule.TimeZone);
        Assert.Equal(TestData.Slot(0, 24).Start, schedule.Start);
        Assert.Equal(TestData.Slot(0, 24).End, schedule.End);
        Assert.Collection(
            schedule.Bays,
            first =>
            {
                Assert.Equal(lift1.Id, first.Bay.Id);
                Assert.Equal([early.Id, late.Id], first.Appointments.Select(a => a.Id).ToArray());
            },
            second =>
            {
                Assert.Equal(lift2.Id, second.Bay.Id);
                Assert.Equal(inLift2.Id, Assert.Single(second.Appointments).Id);
            });
    }

    [Fact]
    public async Task Schedule_leaves_out_cancelled_appointments_and_shows_no_shows_and_check_ins()
    {
        var bay = _store.Seed(TestData.Bay());
        var cancelled = _store.Seed(TestData.Appointment(bay, _vehicle, TestData.Slot(8, 1)));
        cancelled.Cancel("Customer is ill", FixedTimeProvider.Default);
        var noShow = _store.Seed(TestData.Appointment(bay, _vehicle, TestData.Slot(9, 1)));
        noShow.MarkNoShow(TestData.Slot(9, 1).Start);
        var checkedIn = _store.Seed(TestData.Appointment(bay, _vehicle, TestData.Slot(10, 1)));
        checkedIn.CheckIn(Guid.NewGuid(), TestData.Slot(10, 1).Start);

        var result = await _handler.HandleAsync(new GetDayScheduleQuery(Day, GetDayScheduleQuery.Utc));

        var row = Assert.Single(result.Value.Bays);
        Assert.Equal(["NoShow", "CheckedIn"], row.Appointments.Select(a => a.Status).ToArray());
    }

    [Fact]
    public async Task Schedule_lists_bays_out_of_service_and_bays_without_appointments()
    {
        var idle = _store.Seed(TestData.Bay("Lift 1"));
        var closed = _store.Seed(TestData.Bay("Lift 2"));
        var stranded = _store.Seed(TestData.Appointment(closed, _vehicle, TestData.Slot(9, 1)));
        closed.TakeOutOfService();

        var result = await _handler.HandleAsync(new GetDayScheduleQuery(Day, GetDayScheduleQuery.Utc));

        Assert.Collection(
            result.Value.Bays,
            first =>
            {
                Assert.Equal(idle.Id, first.Bay.Id);
                Assert.Empty(first.Appointments);
            },
            second =>
            {
                Assert.False(second.Bay.IsInService);
                Assert.Equal(stranded.Id, Assert.Single(second.Appointments).Id);
            });
    }

    [Fact]
    public async Task Appointment_running_past_midnight_appears_on_both_days()
    {
        var bay = _store.Seed(TestData.Bay());
        var overnight = _store.Seed(TestData.Appointment(bay, _vehicle, TestData.Slot(22, 4)));

        var first = await _handler.HandleAsync(new GetDayScheduleQuery(Day, GetDayScheduleQuery.Utc));
        var second = await _handler.HandleAsync(new GetDayScheduleQuery(Day.AddDays(1), GetDayScheduleQuery.Utc));

        Assert.Equal(overnight.Id, Assert.Single(Assert.Single(first.Value.Bays).Appointments).Id);
        Assert.Equal(overnight.Id, Assert.Single(Assert.Single(second.Value.Bays).Appointments).Id);
    }

    [Fact]
    public async Task Day_is_read_in_the_given_time_zone()
    {
        var bay = _store.Seed(TestData.Bay());
        var justAfterLocalMidnight = _store.Seed(TestData.Appointment(bay, _vehicle, TestData.Slot(-2, 1)));
        _store.Seed(TestData.Appointment(bay, _vehicle, TestData.Slot(22, 1))); // 00:00 next day in Berlin

        var result = await _handler.HandleAsync(new GetDayScheduleQuery(Day, "Europe/Berlin"));

        // 21 October 2026 in Berlin is CEST (UTC+2): 22:00 UTC the day before until 22:00 UTC.
        Assert.Equal("Europe/Berlin", result.Value.TimeZone);
        Assert.Equal(TestData.Slot(-2, 24).Start, result.Value.Start);
        Assert.Equal(TestData.Slot(-2, 24).End, result.Value.End);
        Assert.Equal(justAfterLocalMidnight.Id, Assert.Single(Assert.Single(result.Value.Bays).Appointments).Id);
    }

    [Fact]
    public async Task Day_on_which_clocks_go_back_lasts_25_hours()
    {
        // Central European Summer Time ends on 25 October 2026 at 03:00 local time.
        var result = await _handler.HandleAsync(new GetDayScheduleQuery(new DateOnly(2026, 10, 25), "Europe/Berlin"));

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), result.Value.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), result.Value.End);
        Assert.Equal(TimeSpan.FromHours(25), result.Value.End - result.Value.Start);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Unknown_time_zone_returns_a_validation_error(string timeZone)
    {
        var result = await _handler.HandleAsync(new GetDayScheduleQuery(Day, timeZone));

        Assert.True(result.IsFailure);
        Assert.Equal("schedule.unknown_time_zone", result.Error.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public async Task Schedule_of_a_workshop_without_bays_is_empty()
    {
        var result = await _handler.HandleAsync(new GetDayScheduleQuery(Day, GetDayScheduleQuery.Utc));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Bays);
    }
}
