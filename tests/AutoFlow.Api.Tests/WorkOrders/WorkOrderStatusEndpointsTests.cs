using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.WorkOrders;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.WorkOrders;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class WorkOrderStatusEndpointsTests : IDisposable
{
    private readonly HttpClient _client;

    public WorkOrderStatusEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Approve_records_the_total_the_customer_agreed_to()
    {
        var workOrder = await OpenWithLabourAsync();

        using var response = await PostActionAsync(workOrder.Id, "approve");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await response.Content.ReadFromJsonAsync<WorkOrderDetails>();
        Assert.Equal("Approved", approved!.Status);
        Assert.Equal(120m, approved.ApprovedTotal);
        Assert.NotNull(approved.ApprovedAt);

        var loaded = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{workOrder.Id}"));
        Assert.Equal(("Approved", 120m), (loaded!.Status, loaded.ApprovedTotal));
    }

    [Fact]
    public async Task Approve_without_lines_is_rejected_with_422()
    {
        var workOrder = await OpenAsync();

        using var response = await PostActionAsync(workOrder.Id, "approve");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task Start_of_a_draft_is_rejected_with_422_and_changes_nothing()
    {
        var workOrder = await OpenWithLabourAsync();

        using var response = await PostActionAsync(workOrder.Id, "start");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        var loaded = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{workOrder.Id}"));
        Assert.Equal("Draft", loaded!.Status);
        Assert.Null(loaded.StartedAt);
    }

    [Fact]
    public async Task Start_and_complete_move_an_approved_work_order_to_completed()
    {
        var workOrder = await OpenWithLabourAsync();
        (await PostActionAsync(workOrder.Id, "approve")).Dispose();

        using var started = await PostActionAsync(workOrder.Id, "start");
        Assert.Equal("InProgress", (await started.Content.ReadFromJsonAsync<WorkOrderDetails>())!.Status);

        using var completed = await PostActionAsync(workOrder.Id, "complete");

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var done = await completed.Content.ReadFromJsonAsync<WorkOrderDetails>();
        Assert.Equal("Completed", done!.Status);
        Assert.NotNull(done.StartedAt);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Lines_of_a_completed_work_order_can_no_longer_be_changed()
    {
        var workOrder = await OpenWithLabourAsync();
        foreach (var action in new[] { "approve", "start", "complete" })
        {
            using var moved = await PostActionAsync(workOrder.Id, action);
            moved.EnsureSuccessStatusCode();
        }

        using var added = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/labour-lines"),
            new { description = "Forgotten test drive", hours = 0.25m, hourlyRate = 80m });
        using var removed = await _client.DeleteAsync(
            Url($"/api/work-orders/{workOrder.Id}/lines/{workOrder.Lines[0].Id}"));

        await AssertProblemAsync(added, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        await AssertProblemAsync(removed, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        var loaded = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{workOrder.Id}"));
        Assert.Equal(workOrder.Lines[0].Id, Assert.Single(loaded!.Lines).Id);
    }

    [Fact]
    public async Task Cancel_withdraws_a_draft_with_its_reason()
    {
        var workOrder = await OpenAsync();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/cancel"),
            new { reason = "Customer declined the estimate" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<WorkOrderDetails>();
        Assert.Equal(("Cancelled", "Customer declined the estimate"), (cancelled!.Status, cancelled.CancellationReason));
        Assert.NotNull(cancelled.CancelledAt);
    }

    [Fact]
    public async Task Cancel_without_a_reason_returns_400()
    {
        var workOrder = await OpenAsync();

        using var response = await _client.PostAsJsonAsync(Url($"/api/work-orders/{workOrder.Id}/cancel"), new { reason = "" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Cancel_after_work_has_started_is_rejected_with_422()
    {
        var workOrder = await OpenWithLabourAsync();
        (await PostActionAsync(workOrder.Id, "approve")).Dispose();
        (await PostActionAsync(workOrder.Id, "start")).Dispose();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/cancel"),
            new { reason = "Customer changed their mind" });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("start")]
    [InlineData("complete")]
    public async Task Action_on_unknown_work_order_returns_404(string action)
    {
        using var response = await PostActionAsync(Guid.NewGuid(), action);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "work_order.not_found");
    }

    public void Dispose() => _client.Dispose();

    private Task<HttpResponseMessage> PostActionAsync(Guid workOrderId, string action) =>
        _client.PostAsync(Url($"/api/work-orders/{workOrderId}/{action}"), content: null);

    private async Task<WorkOrderDetails> OpenAsync()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        return await _client.OpenWorkOrderAsync(vehicle.Id);
    }

    /// <summary>A draft with one labour line of 1.5 h at 80.00 (120.00 net).</summary>
    private async Task<WorkOrderDetails> OpenWithLabourAsync()
    {
        var workOrder = await OpenAsync();
        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/labour-lines"),
            new { description = "Replace front brake pads", hours = 1.5m, hourlyRate = 80m });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDetails>())!;
    }
}
