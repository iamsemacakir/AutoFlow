using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.WorkOrders;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Workflows;

/// <summary>
/// A planned visit told only through HTTP: the advisor books a bay, the
/// customer asks for a later time, the day's schedule shows the plan, and
/// when the car arrives the appointment turns into a work order.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class AppointmentWorkflowTests : IDisposable
{
    private readonly HttpClient _client;

    public AppointmentWorkflowTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.CreateClient();
    }

    [Fact]
    public async Task A_booked_visit_is_moved_shown_on_the_schedule_and_checked_in()
    {
        var bay = await _client.CreateBayAsync();
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id, mileageKm: 61_300);
        var day = DateOnly.FromDateTime(InAWeek(0).UtcDateTime);
        var scheduleUrl = Url(string.Create(CultureInfo.InvariantCulture, $"/api/schedule?date={day:yyyy-MM-dd}"));

        // 1. The advisor books the morning slot.
        using var booked = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/appointments"),
            new { bayId = bay.Id, start = InAWeek(9), end = InAWeek(10), purpose = "Timing belt and water pump" });
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var appointmentUrl = booked.Headers.Location!;

        // 2. The customer calls: the afternoon suits better, and the job needs longer.
        using var moved = await _client.PostAsJsonAsync(
            At(appointmentUrl, "reschedule"),
            new { bayId = bay.Id, start = InAWeek(13), end = InAWeek(16) });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        // 3. The day's plan shows the bay taken for the afternoon.
        var plan = await _client.GetFromJsonAsync<DaySchedule>(scheduleUrl);
        var planned = Assert.Single(Assert.Single(plan!.Bays, b => b.Bay.Id == bay.Id).Appointments);
        Assert.Equal(("Scheduled", InAWeek(13), InAWeek(16)), (planned.Status, planned.Start, planned.End));

        // 4. The car arrives: checking in opens the work order with the odometer reading.
        using var arrived = await _client.PostAsJsonAsync(At(appointmentUrl, "check-in"), new { mileageKm = 61_480 });
        Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
        var checkedIn = (await arrived.Content.ReadFromJsonAsync<CheckedInAppointment>())!;
        Assert.Equal("Timing belt and water pump", checkedIn.WorkOrder.Complaint);
        Assert.Equal(61_480, checkedIn.WorkOrder.MileageKmAtIntake);

        // 5. From here on it is an ordinary work order: estimate and approval.
        var workOrderUrl = Url($"/api/work-orders/{checkedIn.WorkOrder.Id}");
        using var labour = await _client.PostAsJsonAsync(
            At(workOrderUrl, "labour-lines"),
            new { description = "Replace timing belt and water pump", hours = 3.5m, hourlyRate = 95m });
        using var approved = await _client.PostAsync(At(workOrderUrl, "approve"), content: null);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (labour.StatusCode, approved.StatusCode));
        Assert.Equal(332.5m, (await approved.Content.ReadFromJsonAsync<WorkOrderDetails>())!.ApprovedTotal);

        // 6. The schedule now links the visit to its work order, and the
        //    appointment can no longer be moved or called off.
        var afterArrival = await _client.GetFromJsonAsync<DaySchedule>(scheduleUrl);
        var visit = Assert.Single(Assert.Single(afterArrival!.Bays, b => b.Bay.Id == bay.Id).Appointments);
        Assert.Equal("CheckedIn", visit.Status);
        Assert.Equal(checkedIn.WorkOrder.Id, visit.WorkOrderId);

        using var tooLate = await _client.PostAsJsonAsync(At(appointmentUrl, "cancel"), new { reason = "Changed their mind" });
        await AssertProblemAsync(tooLate, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    public void Dispose() => _client.Dispose();

    /// <summary>A sub-path of a URL taken from a <c>Location</c> header (which may be absolute).</summary>
    private static Uri At(Uri resource, string segment) =>
        new($"{resource.OriginalString.TrimEnd('/')}/{segment}", UriKind.RelativeOrAbsolute);
}
