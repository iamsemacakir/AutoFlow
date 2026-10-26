using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Vehicles;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Vehicles;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class VehicleEndpointsTests : IDisposable
{
    private readonly HttpClient _client;

    public VehicleEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Post_registers_a_vehicle_that_can_be_read_from_its_location()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vin = UniqueVin();

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/customers/{owner.Id}/vehicles"),
            new { vin = vin.ToLowerInvariant(), licensePlate = "m-ab  123", make = "Volkswagen", model = "Golf", modelYear = 2019, mileageKm = 50_000 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VehicleDetails>();
        Assert.NotNull(created);
        Assert.Equal(owner.Id, created.CustomerId);
        Assert.Equal(vin, created.Vin);
        Assert.Equal("M-AB 123", created.LicensePlate);
        Assert.EndsWith($"/api/vehicles/{created.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var loaded = await _client.GetFromJsonAsync<VehicleDetails>(response.Headers.Location);

        Assert.NotNull(loaded);
        Assert.Equal(created with { RegisteredAt = loaded.RegisteredAt }, loaded);
        AssertSameInstant(created.RegisteredAt, loaded.RegisteredAt);
    }

    [Fact]
    public async Task Post_for_unknown_customer_returns_404()
    {
        using var response = await PostVehicleAsync(Guid.NewGuid(), UniqueVin());

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "customer.not_found");
    }

    [Fact]
    public async Task Post_with_a_vin_that_is_already_registered_returns_409()
    {
        var first = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var second = await _client.CreateCustomerAsync("Ben", "Wagner");
        var vin = UniqueVin();
        using var registered = await PostVehicleAsync(first.Id, vin);
        registered.EnsureSuccessStatusCode();

        using var response = await PostVehicleAsync(second.Id, vin);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "vehicle.duplicate_vin");
    }

    [Fact]
    public async Task Post_with_a_malformed_vin_is_rejected_with_422()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");

        using var response = await PostVehicleAsync(owner.Id, "TOO-SHORT");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task Get_customer_vehicles_lists_only_that_customers_vehicles()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var other = await _client.CreateCustomerAsync("Ben", "Wagner");
        var first = await RegisterAsync(owner.Id);
        var second = await RegisterAsync(owner.Id);
        await RegisterAsync(other.Id);

        var vehicles = await _client.GetFromJsonAsync<List<VehicleDetails>>(Url($"/api/customers/{owner.Id}/vehicles"));

        Assert.NotNull(vehicles);
        Assert.Equal(new[] { first.Id, second.Id }, vehicles.Select(v => v.Id));
    }

    [Fact]
    public async Task Get_vehicles_of_unknown_customer_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/customers/{Guid.NewGuid()}/vehicles"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "customer.not_found");
    }

    [Fact]
    public async Task Get_unknown_vehicle_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/vehicles/{Guid.NewGuid()}"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "vehicle.not_found");
    }

    [Fact]
    public async Task Put_mileage_stores_the_new_reading()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await RegisterAsync(owner.Id);

        using var response = await _client.PutAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/mileage"),
            new { mileageKm = 62_500 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loaded = await _client.GetFromJsonAsync<VehicleDetails>(Url($"/api/vehicles/{vehicle.Id}"));
        Assert.NotNull(loaded);
        Assert.Equal(62_500, loaded.MileageKm);
    }

    [Fact]
    public async Task Put_mileage_lower_than_the_last_reading_is_rejected_with_422()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await RegisterAsync(owner.Id);

        using var response = await _client.PutAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/mileage"),
            new { mileageKm = vehicle.MileageKm - 1 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var loaded = await _client.GetFromJsonAsync<VehicleDetails>(Url($"/api/vehicles/{vehicle.Id}"));
        Assert.NotNull(loaded);
        Assert.Equal(vehicle.MileageKm, loaded.MileageKm);
    }

    [Fact]
    public async Task Put_mileage_of_unknown_vehicle_returns_404()
    {
        using var response = await _client.PutAsJsonAsync(
            Url($"/api/vehicles/{Guid.NewGuid()}/mileage"),
            new { mileageKm = 1_000 });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "vehicle.not_found");
    }

    [Fact]
    public async Task Post_transfer_moves_the_vehicle_to_the_new_owner()
    {
        var seller = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var buyer = await _client.CreateCustomerAsync("Ben", "Wagner");
        var vehicle = await RegisterAsync(seller.Id);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/transfer"),
            new { customerId = buyer.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sellerVehicles = await _client.GetFromJsonAsync<List<VehicleDetails>>(Url($"/api/customers/{seller.Id}/vehicles"));
        var buyerVehicles = await _client.GetFromJsonAsync<List<VehicleDetails>>(Url($"/api/customers/{buyer.Id}/vehicles"));
        Assert.Empty(sellerVehicles!);
        Assert.Equal(vehicle.Id, Assert.Single(buyerVehicles!).Id);
    }

    [Fact]
    public async Task Post_transfer_to_unknown_customer_returns_404()
    {
        var seller = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await RegisterAsync(seller.Id);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/transfer"),
            new { customerId = Guid.NewGuid() });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "customer.not_found");
    }

    [Fact]
    public async Task Post_transfer_to_the_current_owner_is_rejected_with_422()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await RegisterAsync(owner.Id);

        using var response = await _client.PostAsJsonAsync(
            Url($"/api/vehicles/{vehicle.Id}/transfer"),
            new { customerId = owner.Id });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    public void Dispose() => _client.Dispose();

    private Task<HttpResponseMessage> PostVehicleAsync(Guid customerId, string vin) =>
        _client.PostAsJsonAsync(
            Url($"/api/customers/{customerId}/vehicles"),
            new { vin, licensePlate = "B-JB 42", make = "BMW", model = "320d", modelYear = 2021, mileageKm = 30_000 });

    private async Task<VehicleDetails> RegisterAsync(Guid customerId)
    {
        using var response = await PostVehicleAsync(customerId, UniqueVin());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetails>())!;
    }
}
