using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Workflows;

/// <summary>
/// A whole front-desk story told only through HTTP, the way the web client
/// will use the API: follow <c>Location</c> headers, read what was written,
/// and check that every step leaves the other resources consistent.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class VehicleOwnershipWorkflowTests : IDisposable
{
    private readonly HttpClient _client;

    public VehicleOwnershipWorkflowTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task A_car_is_serviced_sold_and_its_former_owner_removed()
    {
        var token = "w" + Guid.NewGuid().ToString("N")[..12];

        // 1. A new customer walks in and is registered.
        using var createdSeller = await _client.PostAsJsonAsync(
            Url("/api/customers"),
            new { firstName = "Anna", lastName = token + "Schmidt", email = "anna@example.com", phoneNumber = "+49 89 123456" });
        Assert.Equal(HttpStatusCode.Created, createdSeller.StatusCode);
        var seller = (await _client.GetFromJsonAsync<CustomerDetails>(createdSeller.Headers.Location))!;

        // 2. The front desk finds her by name.
        var found = await _client.GetFromJsonAsync<PagedList<CustomerDetails>>(Url($"/api/customers?search={token}"));
        Assert.Equal(seller.Id, Assert.Single(found!.Items).Id);

        // 3. Her car is registered under her and gets its own URL.
        var vin = UniqueVin();
        using var registered = await _client.PostAsJsonAsync(
            Url($"/api/customers/{seller.Id}/vehicles"),
            new { vin, licensePlate = "M-AS 2019", make = "Volkswagen", model = "Golf", modelYear = 2019, mileageKm = 48_200 });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var vehicleUrl = registered.Headers.Location!;

        // 4. Two services later the odometer has moved on; an earlier, mistyped reading is refused.
        using var firstService = await _client.PutAsJsonAsync(At(vehicleUrl, "mileage"), new { mileageKm = 55_000 });
        using var secondService = await _client.PutAsJsonAsync(At(vehicleUrl, "mileage"), new { mileageKm = 61_300 });
        using var typo = await _client.PutAsJsonAsync(At(vehicleUrl, "mileage"), new { mileageKm = 6_130 });
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (firstService.StatusCode, secondService.StatusCode));
        await AssertProblemAsync(typo, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");

        // 5. She cannot be removed while she still owns the car.
        using var refused = await _client.DeleteAsync(Url($"/api/customers/{seller.Id}"));
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "customer.has_vehicles");

        // 6. She sells it; the buyer is registered and the car is transferred.
        var buyer = await _client.CreateCustomerAsync("Ben", token + "Wagner");
        using var transferred = await _client.PostAsJsonAsync(
            At(vehicleUrl, "transfer"),
            new { customerId = buyer.Id });
        Assert.Equal(HttpStatusCode.OK, transferred.StatusCode);

        // 7. The car keeps its URL, VIN and mileage history under the new owner.
        using var vehicleJson = JsonDocument.Parse(await _client.GetStringAsync(vehicleUrl));
        Assert.Equal(
            "customerId id licensePlate make mileageKm model modelYear registeredAt vin",
            string.Join(' ', vehicleJson.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        var vehicle = vehicleJson.RootElement.Deserialize<VehicleDetails>(JsonSerializerOptions.Web)!;
        Assert.Equal((buyer.Id, vin, 61_300), (vehicle.CustomerId, vehicle.Vin, vehicle.MileageKm));

        var sellerVehicles = await _client.GetFromJsonAsync<List<VehicleDetails>>(Url($"/api/customers/{seller.Id}/vehicles"));
        var buyerVehicles = await _client.GetFromJsonAsync<List<VehicleDetails>>(Url($"/api/customers/{buyer.Id}/vehicles"));
        Assert.Empty(sellerVehicles!);
        Assert.Equal(vehicle.Id, Assert.Single(buyerVehicles!).Id);

        // 8. Now the former owner can be removed; the buyer and the car remain.
        using var deleted = await _client.DeleteAsync(Url($"/api/customers/{seller.Id}"));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var goneSeller = await _client.GetAsync(Url($"/api/customers/{seller.Id}"));
        await AssertProblemAsync(goneSeller, HttpStatusCode.NotFound, "customer.not_found");

        var remaining = await _client.GetFromJsonAsync<PagedList<CustomerDetails>>(Url($"/api/customers?search={token}"));
        Assert.Equal(buyer.Id, Assert.Single(remaining!.Items).Id);
        using var stillThere = await _client.GetAsync(vehicleUrl);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>A sub-path of a URL taken from a <c>Location</c> header (which may be absolute).</summary>
    private static Uri At(Uri resource, string segment) =>
        new($"{resource.OriginalString.TrimEnd('/')}/{segment}", UriKind.RelativeOrAbsolute);
}
