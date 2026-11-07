using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Common;
using AutoFlow.Application.WorkOrders;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Workflows;

/// <summary>
/// A car's visit to the workshop told only through HTTP: intake, estimate,
/// approval, an extra part found during the job, completion — then the
/// customer and the vehicle's history reflect the job.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class WorkOrderWorkflowTests : IDisposable
{
    private readonly HttpClient _client;

    public WorkOrderWorkflowTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task A_brake_job_is_estimated_approved_extended_and_completed()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id, mileageKm: 48_200);

        // 1. Intake: the front desk opens a work order and records the odometer.
        using var opened = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/work-orders"),
            new { complaint = "Grinding noise when braking", mileageKm = 48_950 });
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var workOrderUrl = opened.Headers.Location!;

        // 2. The estimate: labour and parts.
        using var labour = await _client.PostAsJsonAsync(
            At(workOrderUrl, "labour-lines"),
            new { description = "Replace front pads and discs", hours = 1.5m, hourlyRate = 95m });
        using var pads = await _client.PostAsJsonAsync(
            At(workOrderUrl, "part-lines"),
            new { partNumber = "0 986 494 xyz", description = "Brake pad set, front", quantity = 1m, unitPrice = 64.9m });
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (labour.StatusCode, pads.StatusCode));

        // 3. The customer agrees on the phone; the mechanic starts.
        using var approved = await _client.PostAsync(At(workOrderUrl, "approve"), content: null);
        using var started = await _client.PostAsync(At(workOrderUrl, "start"), content: null);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (approved.StatusCode, started.StatusCode));

        // 4. The discs turn out to be worn too; the extra part stays visible
        //    as a deviation from the approved total.
        using var discs = await _client.PostAsJsonAsync(
            At(workOrderUrl, "part-lines"),
            new { description = "Brake disc, front", quantity = 2m, unitPrice = 48.5m });
        var extended = (await discs.Content.ReadFromJsonAsync<WorkOrderDetails>())!;
        Assert.Equal((142.5m, 161.9m, 304.4m), (extended.LabourTotal, extended.PartsTotal, extended.Total));
        Assert.Equal(207.4m, extended.ApprovedTotal);

        // 5. Done: the lines are final and cancelling is no longer possible.
        using var completed = await _client.PostAsync(At(workOrderUrl, "complete"), content: null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var cancel = await _client.PostAsJsonAsync(At(workOrderUrl, "cancel"), new { reason = "Too expensive" });
        await AssertProblemAsync(cancel, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");

        var final = (await _client.GetFromJsonAsync<WorkOrderDetails>(workOrderUrl))!;
        Assert.Equal(("Completed", 48_950, 304.4m), (final.Status, final.MileageKmAtIntake, final.Total));
        Assert.Equal("1 2 3", string.Join(' ', final.Lines.Select(line => line.Number)));
        Assert.True(final.ApprovedAt <= final.StartedAt && final.StartedAt <= final.CompletedAt);

        // 6. The vehicle's history shows the job; the customer cannot be deleted
        //    while she still owns the car.
        var history = await _client.GetFromJsonAsync<PagedList<WorkOrderDetails>>(Url($"/api/vehicles/{vehicle.Id}/work-orders"));
        Assert.Equal((final.Id, "Completed"), (Assert.Single(history!.Items).Id, history.Items[0].Status));
        using var delete = await _client.DeleteAsync(Url($"/api/customers/{owner.Id}"));
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>A sub-path of a URL taken from a <c>Location</c> header (which may be absolute).</summary>
    private static Uri At(Uri resource, string segment) =>
        new($"{resource.OriginalString.TrimEnd('/')}/{segment}", UriKind.RelativeOrAbsolute);
}
