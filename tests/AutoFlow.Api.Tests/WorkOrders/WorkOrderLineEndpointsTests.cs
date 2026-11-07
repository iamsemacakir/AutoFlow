using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoFlow.Application.WorkOrders;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.WorkOrders;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class WorkOrderLineEndpointsTests : IDisposable
{
    private readonly HttpClient _client;

    public WorkOrderLineEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Post_labour_line_adds_it_and_answers_with_the_new_totals()
    {
        var workOrder = await OpenAsync();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/labour-lines"),
            new { description = "Replace front brake pads", hours = 1.5m, hourlyRate = 80m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkOrderDetails>();
        var line = Assert.Single(updated!.Lines);
        Assert.Equal((1, "Labour", 1.5m, 80m, 120m), (line.Number, line.Kind, line.Quantity, line.UnitPrice, line.Total));
        Assert.Null(line.PartNumber);
        Assert.Equal((120m, 0m, 120m), (updated.LabourTotal, updated.PartsTotal, updated.Total));

        var loaded = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{workOrder.Id}"));
        Assert.Equal(line, Assert.Single(loaded!.Lines));
    }

    [Fact]
    public async Task Post_part_line_normalizes_the_part_number_and_rounds_the_total_to_cents()
    {
        var workOrder = await OpenAsync();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/part-lines"),
            new { partNumber = " 5w30-ll ", description = "Engine oil 5W-30", quantity = 4.5m, unitPrice = 12.99m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkOrderDetails>();
        var line = Assert.Single(updated!.Lines);
        Assert.Equal(("Part", "5W30-LL"), (line.Kind, line.PartNumber));
        Assert.Equal(58.46m, line.Total);
        Assert.Equal((0m, 58.46m, 58.46m), (updated.LabourTotal, updated.PartsTotal, updated.Total));
    }

    [Fact]
    public async Task Post_line_without_its_numbers_returns_400_listing_every_missing_field()
    {
        var workOrder = await OpenAsync();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/part-lines"),
            new { description = "" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fields = problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name);
        Assert.Equal(new[] { "description", "quantity", "unitPrice" }, fields.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Post_line_with_a_fraction_of_a_cent_is_rejected_with_422()
    {
        var workOrder = await OpenAsync();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/labour-lines"),
            new { description = "Diagnosis", hours = 0.5m, hourlyRate = 80.005m });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        var loaded = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{workOrder.Id}"));
        Assert.Empty(loaded!.Lines);
    }

    [Fact]
    public async Task Post_line_on_unknown_work_order_returns_404()
    {
        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{Guid.NewGuid()}/labour-lines"),
            new { description = "Diagnosis", hours = 0.5m, hourlyRate = 80m });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "work_order.not_found");
    }

    [Fact]
    public async Task Delete_line_removes_it_and_the_remaining_lines_keep_their_numbers()
    {
        var workOrder = await OpenAsync();
        await AddLabourAsync(workOrder.Id, "Diagnosis", 0.5m);
        var withTwo = await AddLabourAsync(workOrder.Id, "Replace brake pads", 1.5m);
        var first = withTwo.Lines[0];

        using var response = await _client.DeleteAsync(Url($"/api/work-orders/{workOrder.Id}/lines/{first.Id}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkOrderDetails>();
        var remaining = Assert.Single(updated!.Lines);
        Assert.Equal((2, "Replace brake pads"), (remaining.Number, remaining.Description));
        Assert.Equal(120m, updated.Total);

        var withNext = await AddLabourAsync(workOrder.Id, "Test drive", 0.25m);
        Assert.Equal(new[] { 2, 3 }, withNext.Lines.Select(line => line.Number).ToArray());
    }

    [Fact]
    public async Task Delete_unknown_line_returns_404()
    {
        var workOrder = await OpenAsync();

        using var response = await _client.DeleteAsync(Url($"/api/work-orders/{workOrder.Id}/lines/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "work_order.line_not_found");
    }

    [Fact]
    public async Task Delete_line_of_unknown_work_order_returns_404()
    {
        using var response = await _client.DeleteAsync(Url($"/api/work-orders/{Guid.NewGuid()}/lines/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "work_order.not_found");
    }

    public void Dispose() => _client.Dispose();

    private async Task<WorkOrderDetails> OpenAsync()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        return await _client.OpenWorkOrderAsync(vehicle.Id);
    }

    private async Task<WorkOrderDetails> AddLabourAsync(Guid workOrderId, string description, decimal hours)
    {
        using var response = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrderId}/labour-lines"),
            new { description, hours, hourlyRate = 80m });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderDetails>())!;
    }
}
