using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Appointments;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class AppointmentStatusEndpointsTests : IDisposable
{
    private readonly ApiDatabaseFixture _api;
    private readonly HttpClient _client;

    public AppointmentStatusEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _client = api.CreateClient();
    }

    [Fact]
    public async Task Check_in_opens_a_draft_work_order_and_records_the_reading()
    {
        var (appointment, vehicle) = await BookAsync(_client);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/appointments/{appointment.Id}/check-in"),
            new { mileageKm = vehicle.MileageKm + 500, complaint = "Rattle at the rear axle" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var checkedIn = await response.Content.ReadFromJsonAsync<CheckedInAppointment>();
        Assert.NotNull(checkedIn);
        Assert.Equal("CheckedIn", checkedIn.Appointment.Status);
        Assert.NotNull(checkedIn.Appointment.CheckedInAt);
        Assert.Equal(checkedIn.WorkOrder.Id, checkedIn.Appointment.WorkOrderId);
        Assert.Equal("Draft", checkedIn.WorkOrder.Status);
        Assert.Equal("Rattle at the rear axle", checkedIn.WorkOrder.Complaint);
        Assert.Equal(vehicle.MileageKm + 500, checkedIn.WorkOrder.MileageKmAtIntake);
        Assert.Equal((vehicle.Id, vehicle.CustomerId), (checkedIn.WorkOrder.VehicleId, checkedIn.WorkOrder.CustomerId));

        var workOrder = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{checkedIn.WorkOrder.Id}"));
        Assert.Equal("Draft", workOrder!.Status);
        var reloadedVehicle = await _client.GetFromJsonAsync<VehicleDetails>(Url($"/api/vehicles/{vehicle.Id}"));
        Assert.Equal(vehicle.MileageKm + 500, reloadedVehicle!.MileageKm);
    }

    [Fact]
    public async Task Check_in_without_a_body_uses_the_purpose_and_the_last_reading()
    {
        var (appointment, vehicle) = await BookAsync(_client);

        using var response = await PostActionAsync(_client, appointment.Id, "check-in");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var checkedIn = await response.Content.ReadFromJsonAsync<CheckedInAppointment>();
        Assert.Equal(appointment.Purpose, checkedIn!.WorkOrder.Complaint);
        Assert.Equal(vehicle.MileageKm, checkedIn.WorkOrder.MileageKmAtIntake);
    }

    [Fact]
    public async Task Check_in_twice_returns_422_and_opens_no_second_work_order()
    {
        var (appointment, vehicle) = await BookAsync(_client);
        (await PostActionAsync(_client, appointment.Id, "check-in")).Dispose();

        using var response = await PostActionAsync(_client, appointment.Id, "check-in");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        using var history = await _client.GetAsync(Url($"/api/vehicles/{vehicle.Id}/work-orders"));
        var page = await history.Content.ReadFromJsonAsync<PagedList<WorkOrderDetails>>();
        Assert.Equal(1, page!.TotalCount);
    }

    [Fact]
    public async Task Check_in_with_a_negative_reading_returns_400()
    {
        var (appointment, _) = await BookAsync(_client);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/appointments/{appointment.Id}/check-in"),
            new { mileageKm = -1 });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Cancel_frees_the_slot_for_another_booking()
    {
        var (appointment, vehicle) = await BookAsync(_client);
        var other = await _client.RegisterVehicleAsync(vehicle.CustomerId);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/appointments/{appointment.Id}/cancel"),
            new { reason = "Customer is ill" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<AppointmentDetails>();
        Assert.Equal(("Cancelled", "Customer is ill"), (cancelled!.Status, cancelled.CancellationReason));
        Assert.NotNull(cancelled.CancelledAt);

        var rebooked = await _client.BookAppointmentAsync(other.Id, appointment.BayId, appointment.Start, appointment.End);
        Assert.Equal("Scheduled", rebooked.Status);
    }

    [Fact]
    public async Task Cancel_without_a_reason_returns_400()
    {
        var (appointment, _) = await BookAsync(_client);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/appointments/{appointment.Id}/cancel"),
            new { reason = "  " });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Cancel_of_a_checked_in_appointment_returns_422()
    {
        var (appointment, _) = await BookAsync(_client);
        (await PostActionAsync(_client, appointment.Id, "check-in")).Dispose();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/appointments/{appointment.Id}/cancel"),
            new { reason = "Changed their mind" });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task No_show_is_refused_before_the_slot_and_recorded_once_it_has_begun()
    {
        var clock = new ShiftedTimeProvider();
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = _api.AuthorizationFor(UserRole.Owner);
        var (appointment, _) = await BookAsync(client);

        using var early = await PostActionAsync(client, appointment.Id, "no-show");
        await AssertProblemAsync(early, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");

        // Eight days later the slot (a week from now) has begun.
        clock.Shift = TimeSpan.FromDays(8);
        using var response = await PostActionAsync(client, appointment.Id, "no-show");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var noShow = await response.Content.ReadFromJsonAsync<AppointmentDetails>();
        Assert.Equal("NoShow", noShow!.Status);
        Assert.NotNull(noShow.NoShowAt);
    }

    [Theory]
    [InlineData("check-in")]
    [InlineData("no-show")]
    public async Task Action_on_an_unknown_appointment_returns_404(string action)
    {
        using var response = await PostActionAsync(_client, Guid.NewGuid(), action);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "appointment.not_found");
    }

    public void Dispose() => _client.Dispose();

    private static async Task<(AppointmentDetails Appointment, VehicleDetails Vehicle)> BookAsync(HttpClient client)
    {
        BayDetails bay = await client.CreateBayAsync();
        var owner = await client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await client.RegisterVehicleAsync(owner.Id);
        var appointment = await client.BookAppointmentAsync(vehicle.Id, bay.Id, InAWeek(9), InAWeek(10));
        return (appointment, vehicle);
    }

    private static Task<HttpResponseMessage> PostActionAsync(HttpClient client, Guid appointmentId, string action) =>
        client.PostAsync(Url($"/api/appointments/{appointmentId}/{action}"), content: null);
}
