using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Appointments;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Appointments;

/// <summary>
/// Every test books into bays of its own; the schedule lists all bays of the
/// shared database, so assertions look only at the rows of those bays.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class ScheduleEndpointsTests : IDisposable
{
    /// <summary>The UTC day <see cref="ApiTestHelpers.InAWeek"/> books into.</summary>
    private static readonly DateOnly Day = DateOnly.FromDateTime(InAWeek(0).UtcDateTime);

    private readonly HttpClient _client;

    public ScheduleEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Schedule_shows_a_bays_appointments_of_the_day_in_start_order_without_cancelled_ones()
    {
        var bay = await _client.CreateBayAsync();
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var first = await _client.RegisterVehicleAsync(owner.Id);
        var second = await _client.RegisterVehicleAsync(owner.Id);
        var late = await _client.BookAppointmentAsync(first.Id, bay.Id, InAWeek(13), InAWeek(15));
        var early = await _client.BookAppointmentAsync(second.Id, bay.Id, InAWeek(9), InAWeek(10));
        var cancelled = await _client.BookAppointmentAsync(second.Id, bay.Id, InAWeek(11), InAWeek(12));
        (await _client.PostAsJsonAsync(Url($"/api/appointments/{cancelled.Id}/cancel"), new { reason = "Customer is ill" })).Dispose();
        await _client.BookAppointmentAsync(second.Id, bay.Id, InAWeek(24 + 9), InAWeek(24 + 10)); // next day

        var schedule = await GetScheduleAsync(Day);

        Assert.Equal(Day, schedule.Date);
        Assert.Equal("UTC", schedule.TimeZone);
        Assert.Equal((InAWeek(0), InAWeek(24)), (schedule.Start, schedule.End));
        var row = Assert.Single(schedule.Bays, b => b.Bay.Id == bay.Id);
        Assert.Equal([early.Id, late.Id], row.Appointments.Select(a => a.Id).ToArray());
        Assert.All(row.Appointments, a => Assert.Equal("Scheduled", a.Status));
    }

    [Fact]
    public async Task Schedule_reads_the_day_in_the_given_time_zone()
    {
        var bay = await _client.CreateBayAsync();
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);

        // 23:00 UTC is already the next day in Berlin, summer or winter.
        var lateEvening = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(23), InAWeek(23.5));

        var utcDay = await GetScheduleAsync(Day);
        var berlinDay = await GetScheduleAsync(Day, "Europe/Berlin");
        var berlinNextDay = await GetScheduleAsync(Day.AddDays(1), "Europe/Berlin");

        Assert.Equal(lateEvening.Id, Assert.Single(Assert.Single(utcDay.Bays, b => b.Bay.Id == bay.Id).Appointments).Id);
        Assert.Empty(Assert.Single(berlinDay.Bays, b => b.Bay.Id == bay.Id).Appointments);
        Assert.Equal(lateEvening.Id, Assert.Single(Assert.Single(berlinNextDay.Bays, b => b.Bay.Id == bay.Id).Appointments).Id);
        Assert.Equal("Europe/Berlin", berlinDay.TimeZone);
        Assert.True(berlinDay.Start < InAWeek(0), "Berlin's day starts before the UTC day.");
    }

    [Fact]
    public async Task Schedule_lists_bays_without_appointments_and_bays_out_of_service()
    {
        var idle = await _client.CreateBayAsync();
        var closed = await _client.CreateBayAsync();
        (await _client.PostAsync(Url($"/api/bays/{closed.Id}/take-out-of-service"), content: null)).Dispose();

        var schedule = await GetScheduleAsync(Day);

        Assert.Empty(Assert.Single(schedule.Bays, b => b.Bay.Id == idle.Id).Appointments);
        Assert.False(Assert.Single(schedule.Bays, b => b.Bay.Id == closed.Id).Bay.IsInService);
    }

    [Fact]
    public async Task Schedule_in_an_unknown_time_zone_returns_400()
    {
        using var response = await _client.GetAsync(Url("/api/schedule?date=2026-11-20&timeZone=Mars/Olympus_Mons"));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "schedule.unknown_time_zone");
    }

    [Theory]
    [InlineData("/api/schedule")]
    [InlineData("/api/schedule?date=tomorrow")]
    [InlineData("/api/schedule?date=2026-02-30")]
    public async Task Schedule_without_a_valid_date_returns_400(string url)
    {
        using var response = await _client.GetAsync(Url(url));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.malformed");
    }

    public void Dispose() => _client.Dispose();

    private async Task<DaySchedule> GetScheduleAsync(DateOnly date, string? timeZone = null)
    {
        var url = string.Create(CultureInfo.InvariantCulture, $"/api/schedule?date={date:yyyy-MM-dd}");
        if (timeZone is not null)
        {
            url += $"&timeZone={Uri.EscapeDataString(timeZone)}";
        }

        return (await _client.GetFromJsonAsync<DaySchedule>(Url(url)))!;
    }
}
