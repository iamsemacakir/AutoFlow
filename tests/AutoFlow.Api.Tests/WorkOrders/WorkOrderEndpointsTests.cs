using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.WorkOrders;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class WorkOrderEndpointsTests : IDisposable
{
    private readonly HttpClient _client;

    public WorkOrderEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Post_opens_a_draft_for_the_current_owner_that_can_be_read_from_its_location()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id, mileageKm: 30_000);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/work-orders"),
            new { complaint = "Brakes squeal when cold", mileageKm = 31_250 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<WorkOrderDetails>();
        Assert.NotNull(created);
        Assert.Equal(vehicle.Id, created.VehicleId);
        Assert.Equal(owner.Id, created.CustomerId);
        Assert.Equal("Draft", created.Status);
        Assert.Equal(31_250, created.MileageKmAtIntake);
        Assert.Empty(created.Lines);
        Assert.Equal(0m, created.Total);
        Assert.Null(created.ApprovedTotal);
        Assert.EndsWith($"/api/work-orders/{created.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var loaded = await _client.GetFromJsonAsync<WorkOrderDetails>(response.Headers.Location);
        Assert.NotNull(loaded);
        Assert.Equal(created.Complaint, loaded.Complaint);
        AssertSameInstant(created.CreatedAt, loaded.CreatedAt);

        // The intake reading is recorded on the vehicle in the same step.
        var reloadedVehicle = await _client.GetFromJsonAsync<VehicleDetails>(Url($"/api/vehicles/{vehicle.Id}"));
        Assert.Equal(31_250, reloadedVehicle!.MileageKm);
    }

    [Fact]
    public async Task Post_without_a_reading_uses_the_vehicles_last_mileage()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id, mileageKm: 42_000);

        var workOrder = await _client.OpenWorkOrderAsync(vehicle.Id);

        Assert.Equal(42_000, workOrder.MileageKmAtIntake);
    }

    [Fact]
    public async Task Post_for_unknown_vehicle_returns_404()
    {
        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{Guid.NewGuid()}/work-orders"),
            new { complaint = "Oil change" });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "vehicle.not_found");
    }

    [Fact]
    public async Task Post_without_a_complaint_returns_400_before_anything_is_stored()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/work-orders"),
            new { complaint = " ", mileageKm = -1 });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
        var history = await _client.GetFromJsonAsync<PagedList<WorkOrderDetails>>(Url($"/api/vehicles/{vehicle.Id}/work-orders"));
        Assert.Equal(0, history!.TotalCount);
    }

    [Fact]
    public async Task Post_with_a_reading_below_the_vehicles_mileage_is_rejected_with_422()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id, mileageKm: 30_000);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/work-orders"),
            new { complaint = "Oil change", mileageKm = 29_999 });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        var history = await _client.GetFromJsonAsync<PagedList<WorkOrderDetails>>(Url($"/api/vehicles/{vehicle.Id}/work-orders"));
        Assert.Equal(0, history!.TotalCount);
    }

    [Fact]
    public async Task Get_unknown_work_order_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/work-orders/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "work_order.not_found");
    }

    [Fact]
    public async Task Get_history_pages_a_vehicles_work_orders_newest_first()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        var other = await _client.RegisterVehicleAsync(owner.Id);
        var first = await _client.OpenWorkOrderAsync(vehicle.Id, "Annual service");
        var second = await _client.OpenWorkOrderAsync(vehicle.Id, "Rattling exhaust");
        var third = await _client.OpenWorkOrderAsync(vehicle.Id, "Winter tyres");
        await _client.OpenWorkOrderAsync(other.Id);

        var page1 = await _client.GetFromJsonAsync<PagedList<WorkOrderDetails>>(
            Url($"/api/vehicles/{vehicle.Id}/work-orders?page=1&pageSize=2"));
        var page2 = await _client.GetFromJsonAsync<PagedList<WorkOrderDetails>>(
            Url($"/api/vehicles/{vehicle.Id}/work-orders?page=2&pageSize=2"));

        Assert.Equal(new[] { third.Id, second.Id }, page1!.Items.Select(w => w.Id).ToArray());
        Assert.Equal(new[] { first.Id }, page2!.Items.Select(w => w.Id).ToArray());
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
    }

    [Fact]
    public async Task Get_history_keeps_work_orders_commissioned_by_a_former_owner()
    {
        var seller = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var buyer = await _client.CreateCustomerAsync("Ben", "Wagner");
        var vehicle = await _client.RegisterVehicleAsync(seller.Id);
        var before = await _client.OpenWorkOrderAsync(vehicle.Id);
        using var transferred = await _client.PostAsJsonAsync(Url($"/api/vehicles/{vehicle.Id}/transfer"), new { customerId = buyer.Id });
        transferred.EnsureSuccessStatusCode();

        var after = await _client.OpenWorkOrderAsync(vehicle.Id);

        var history = await _client.GetFromJsonAsync<PagedList<WorkOrderDetails>>(Url($"/api/vehicles/{vehicle.Id}/work-orders"));
        Assert.Equal(
            new[] { (after.Id, buyer.Id), (before.Id, seller.Id) },
            history!.Items.Select(w => (w.Id, w.CustomerId)).ToArray());
    }

    [Fact]
    public async Task Get_history_of_unknown_vehicle_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/vehicles/{Guid.NewGuid()}/work-orders"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "vehicle.not_found");
    }

    [Fact]
    public async Task Get_history_with_too_large_a_page_returns_400()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);

        using var response = await _client.GetAsync(
            Url($"/api/vehicles/{vehicle.Id}/work-orders?pageSize={Paging.MaxPageSize + 1}"));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "paging.invalid_page_size");
    }

    public void Dispose() => _client.Dispose();
}
