using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Appointments;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class AppointmentEndpointsTests : IDisposable
{
    private readonly HttpClient _client;

    public AppointmentEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.CreateClient();
    }

    [Fact]
    public async Task Book_answers_201_for_the_current_owner_and_can_be_read_from_its_location()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var start = InAWeek(9).ToOffset(TimeSpan.FromHours(2));

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/appointments"),
            new { bayId = bay.Id, start, end = start.AddHours(1.5), purpose = "Annual service" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booked = await response.Content.ReadFromJsonAsync<AppointmentDetails>();
        Assert.NotNull(booked);
        Assert.Equal(bay.Id, booked.BayId);
        Assert.Equal(vehicle.Id, booked.VehicleId);
        Assert.Equal(vehicle.CustomerId, booked.CustomerId);
        Assert.Equal(InAWeek(9), booked.Start);
        Assert.Equal(TimeSpan.Zero, booked.Start.Offset);
        Assert.Equal(InAWeek(10.5), booked.End);
        Assert.Equal("Scheduled", booked.Status);
        Assert.Null(booked.WorkOrderId);
        Assert.EndsWith($"/api/appointments/{booked.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var loaded = await _client.GetFromJsonAsync<AppointmentDetails>(response.Headers.Location);
        Assert.Equal((booked.Id, booked.Start, booked.End), (loaded!.Id, loaded.Start, loaded.End));
        AssertSameInstant(booked.BookedAt, loaded.BookedAt);
    }

    [Fact]
    public async Task Book_without_required_fields_returns_400_listing_each_of_them()
    {
        var (_, vehicle) = await ArrangeAsync();

        using var response = await _client.PostAsJsonAsync(Url($"/api/vehicles/{vehicle.Id}/appointments"), new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fields = problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal);
        Assert.Equal("bayId end purpose start", string.Join(' ', fields));
    }

    [Fact]
    public async Task Book_for_an_unknown_vehicle_returns_404()
    {
        var bay = await _client.CreateBayAsync();

        using var response = await BookAsync(Guid.NewGuid(), bay.Id, InAWeek(9), InAWeek(10));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "vehicle.not_found");
    }

    [Fact]
    public async Task Book_into_an_unknown_bay_returns_404()
    {
        var (_, vehicle) = await ArrangeAsync();

        using var response = await BookAsync(vehicle.Id, Guid.NewGuid(), InAWeek(9), InAWeek(10));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "bay.not_found");
    }

    [Fact]
    public async Task Book_into_a_bay_out_of_service_returns_409()
    {
        var (bay, vehicle) = await ArrangeAsync();
        (await _client.PostAsync(Url($"/api/bays/{bay.Id}/take-out-of-service"), content: null)).Dispose();

        using var response = await BookAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "appointment.bay_out_of_service");
    }

    [Fact]
    public async Task Book_over_another_vehicle_in_the_bay_returns_409_and_back_to_back_is_fine()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var other = await _client.RegisterVehicleAsync(vehicle.CustomerId);
        await _client.BookAppointmentAsync(other.Id, bay.Id, InAWeek(9), InAWeek(11));

        using var clash = await BookAsync(vehicle.Id, bay.Id, InAWeek(10.75), InAWeek(12));
        using var backToBack = await BookAsync(vehicle.Id, bay.Id, InAWeek(11), InAWeek(12));

        await AssertProblemAsync(clash, HttpStatusCode.Conflict, "appointment.bay_already_booked");
        Assert.Equal(HttpStatusCode.Created, backToBack.StatusCode);
    }

    [Fact]
    public async Task Book_of_a_vehicle_already_booked_into_another_bay_returns_409()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var otherBay = await _client.CreateBayAsync();
        await _client.BookAppointmentAsync(vehicle.Id, otherBay.Id, InAWeek(8), InAWeek(9.25));

        using var response = await BookAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "appointment.vehicle_already_booked");
    }

    [Fact]
    public async Task Book_off_the_15_minute_grid_returns_422()
    {
        var (bay, vehicle) = await ArrangeAsync();

        using var response = await BookAsync(vehicle.Id, bay.Id, InAWeek(9).AddMinutes(5), InAWeek(10));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task Book_in_the_past_returns_422()
    {
        var (bay, vehicle) = await ArrangeAsync();

        using var response = await BookAsync(vehicle.Id, bay.Id, InAWeek(9).AddDays(-14), InAWeek(10).AddDays(-14));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task Get_of_an_unknown_appointment_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/appointments/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "appointment.not_found");
    }

    [Fact]
    public async Task Reschedule_moves_the_appointment_to_another_bay_and_slot()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var otherBay = await _client.CreateBayAsync();
        var appointment = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));

        using var response = await RescheduleAsync(appointment.Id, otherBay.Id, InAWeek(13), InAWeek(15));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = await response.Content.ReadFromJsonAsync<AppointmentDetails>();
        Assert.Equal((otherBay.Id, InAWeek(13), InAWeek(15)), (moved!.BayId, moved.Start, moved.End));
        Assert.NotNull(moved.RescheduledAt);

        var loaded = await _client.GetFromJsonAsync<AppointmentDetails>(Url($"/api/appointments/{appointment.Id}"));
        Assert.Equal((otherBay.Id, InAWeek(13)), (loaded!.BayId, loaded.Start));
    }

    [Fact]
    public async Task Reschedule_may_overlap_the_appointments_own_current_slot()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var appointment = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(11));

        using var response = await RescheduleAsync(appointment.Id, bay.Id, InAWeek(9), InAWeek(12));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_into_a_clash_returns_409_and_keeps_the_old_slot()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var other = await _client.RegisterVehicleAsync(vehicle.CustomerId);
        await _client.BookAppointmentAsync(other.Id, bay.Id, InAWeek(13), InAWeek(14));
        var appointment = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));

        using var response = await RescheduleAsync(appointment.Id, bay.Id, InAWeek(13.5), InAWeek(14.5));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "appointment.bay_already_booked");
        var loaded = await _client.GetFromJsonAsync<AppointmentDetails>(Url($"/api/appointments/{appointment.Id}"));
        Assert.Equal((InAWeek(9), InAWeek(10)), (loaded!.Start, loaded.End));
        Assert.Null(loaded.RescheduledAt);
    }

    [Fact]
    public async Task Reschedule_to_the_same_bay_and_slot_returns_422()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var appointment = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));

        using var response = await RescheduleAsync(appointment.Id, bay.Id, InAWeek(9), InAWeek(10));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task Reschedule_of_an_unknown_appointment_returns_404()
    {
        var bay = await _client.CreateBayAsync();

        using var response = await RescheduleAsync(Guid.NewGuid(), bay.Id, InAWeek(9), InAWeek(10));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "appointment.not_found");
    }

    [Fact]
    public async Task Reschedule_without_a_bay_returns_400()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var appointment = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/appointments/{appointment.Id}/reschedule"),
            new { start = InAWeek(13), end = InAWeek(14) });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Get_list_shows_the_vehicles_appointments_latest_first_and_by_status()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var first = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));
        var cancelled = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(24 + 9), InAWeek(24 + 10));
        var latest = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(48 + 9), InAWeek(48 + 10));
        var (_, otherVehicle) = await ArrangeAsync();
        await _client.BookAppointmentAsync(otherVehicle.Id, bay.Id, InAWeek(13), InAWeek(14));
        (await _client.PostAsJsonAsync(
            Url($"/api/appointments/{cancelled.Id}/cancel"),
            new { reason = "Customer is ill" })).Dispose();

        var all = await _client.GetFromJsonAsync<PagedList<AppointmentDetails>>(
            Url($"/api/vehicles/{vehicle.Id}/appointments"));
        var scheduled = await _client.GetFromJsonAsync<PagedList<AppointmentDetails>>(
            Url($"/api/vehicles/{vehicle.Id}/appointments?status=scheduled"));
        var secondPage = await _client.GetFromJsonAsync<PagedList<AppointmentDetails>>(
            Url($"/api/vehicles/{vehicle.Id}/appointments?page=2&pageSize=2"));

        Assert.Equal([latest.Id, cancelled.Id, first.Id], all!.Items.Select(a => a.Id).ToArray());
        Assert.Equal(["Scheduled", "Cancelled", "Scheduled"], all.Items.Select(a => a.Status).ToArray());
        Assert.Equal([latest.Id, first.Id], scheduled!.Items.Select(a => a.Id).ToArray());
        Assert.Equal(first.Id, Assert.Single(secondPage!.Items).Id);
        Assert.Equal((3, 2), (secondPage.TotalCount, secondPage.TotalPages));
    }

    [Fact]
    public async Task Get_list_keeps_the_appointments_booked_before_the_vehicle_was_sold()
    {
        var (bay, vehicle) = await ArrangeAsync();
        var booked = await _client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));
        var buyer = await _client.CreateCustomerAsync("Ben", "Wagner");
        using var transferred = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/transfer"),
            new { customerId = buyer.Id });
        transferred.EnsureSuccessStatusCode();

        var page = await _client.GetFromJsonAsync<PagedList<AppointmentDetails>>(
            Url($"/api/vehicles/{vehicle.Id}/appointments"));

        var only = Assert.Single(page!.Items);
        Assert.Equal((booked.Id, vehicle.CustomerId), (only.Id, only.CustomerId));
    }

    [Fact]
    public async Task Get_list_of_an_unknown_vehicle_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/vehicles/{Guid.NewGuid()}/appointments"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "vehicle.not_found");
    }

    [Theory]
    [InlineData("status=Booked", "appointment.unknown_status")]
    [InlineData("page=0", "paging.invalid_page")]
    [InlineData("pageSize=101", "paging.invalid_page_size")]
    public async Task Get_list_with_invalid_filters_returns_400(string queryString, string code)
    {
        var (_, vehicle) = await ArrangeAsync();

        using var response = await _client.GetAsync(Url($"/api/vehicles/{vehicle.Id}/appointments?{queryString}"));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    public void Dispose() => _client.Dispose();

    private async Task<(BayDetails Bay, VehicleDetails Vehicle)> ArrangeAsync()
    {
        var bay = await _client.CreateBayAsync();
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        return (bay, vehicle);
    }

    private Task<HttpResponseMessage> BookAsync(Guid vehicleId, Guid bayId, DateTimeOffset start, DateTimeOffset end) =>
        _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicleId}/appointments"),
            new { bayId, start, end, purpose = "Annual service" });

    private Task<HttpResponseMessage> RescheduleAsync(Guid id, Guid bayId, DateTimeOffset start, DateTimeOffset end) =>
        _client.PostAsJsonAsync(Url($"/api/appointments/{id}/reschedule"), new { bayId, start, end });
}
