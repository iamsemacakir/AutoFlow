using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Vehicles;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Concurrency;

/// <summary>
/// Fires identical or competing requests at the same resource at once. How the
/// requests interleave is up to the scheduler, so the tests assert what must
/// hold for every interleaving: one winner where only one may win, every loser
/// answered with a documented 409/422 problem (never a 500), and a final state
/// that matches the accepted requests.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class ConcurrentRequestTests : IDisposable
{
    private const int Contenders = 8;

    // Losers either see the winner in the use case's pre-check or hit the
    // unique index after passing that check at the same moment.
    private static readonly string[] DuplicateVinCodes = ["vehicle.duplicate_vin", "conflict.duplicate"];

    private readonly HttpClient _client;

    public ConcurrentRequestTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task Simultaneous_registrations_of_one_vin_store_exactly_one_vehicle()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vin = UniqueVin();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, Contenders).Select(async i =>
        {
            using var response = await _client.PostAsJsonAsync(
                Url($"/api/customers/{owner.Id}/vehicles"),
                new { vin, licensePlate = $"M-AB {100 + i}", make = "Volkswagen", model = "Golf", modelYear = 2019, mileageKm = 50_000 });
            return (response.StatusCode, Code: await ReadProblemCodeAsync(response));
        }));

        Assert.Single(outcomes, outcome => outcome.StatusCode == HttpStatusCode.Created);
        Assert.All(
            outcomes.Where(outcome => outcome.StatusCode != HttpStatusCode.Created),
            outcome =>
            {
                Assert.Equal(HttpStatusCode.Conflict, outcome.StatusCode);
                Assert.Contains(outcome.Code, DuplicateVinCodes);
            });

        var vehicles = await _client.GetFromJsonAsync<List<VehicleDetails>>(Url($"/api/customers/{owner.Id}/vehicles"));
        Assert.Equal(vin, Assert.Single(vehicles!).Vin);
    }

    [Fact]
    public async Task Simultaneous_mileage_readings_never_lose_the_highest_accepted_one()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await RegisterVehicleAsync(owner.Id);

        var outcomes = await Task.WhenAll(Enumerable.Range(1, Contenders).Select(async i =>
        {
            var mileageKm = vehicle.MileageKm + (i * 100);
            using var response = await _client.PutAsJsonAsync(Url($"/api/vehicles/{vehicle.Id}/mileage"), new { mileageKm });
            return (Mileage: mileageKm, response.StatusCode, Code: await ReadProblemCodeAsync(response));
        }));

        Assert.All(outcomes, outcome =>
        {
            switch (outcome.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.Conflict:
                    // Loaded the same version as a request that committed first.
                    Assert.Equal("conflict.concurrent_update", outcome.Code);
                    break;
                case HttpStatusCode.UnprocessableEntity:
                    // Loaded after a higher reading had been committed.
                    Assert.Equal("domain.rule_violated", outcome.Code);
                    break;
                default:
                    Assert.Fail($"Reading {outcome.Mileage} answered {(int)outcome.StatusCode} {outcome.Code}.");
                    break;
            }
        });

        // Accepted readings commit one after another and never go backwards,
        // so the stored reading is the highest one that was accepted.
        var accepted = outcomes.Where(outcome => outcome.StatusCode == HttpStatusCode.OK).Select(outcome => outcome.Mileage).ToList();
        Assert.NotEmpty(accepted);
        var stored = await _client.GetFromJsonAsync<VehicleDetails>(Url($"/api/vehicles/{vehicle.Id}"));
        Assert.Equal(accepted.Max(), stored!.MileageKm);
    }

    [Fact]
    public async Task Simultaneous_transfers_leave_the_vehicle_with_exactly_one_new_owner()
    {
        var seller = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await RegisterVehicleAsync(seller.Id);
        var buyers = await Task.WhenAll(Enumerable.Range(0, Contenders / 2)
            .Select(i => _client.CreateCustomerAsync("Buyer", $"Number{i}")));

        var outcomes = await Task.WhenAll(buyers.Select(async buyer =>
        {
            using var response = await _client.PostAsJsonAsync(
                Url($"/api/vehicles/{vehicle.Id}/transfer"),
                new { customerId = buyer.Id });
            return (Buyer: buyer.Id, response.StatusCode, Code: await ReadProblemCodeAsync(response));
        }));

        // Every buyer differs from every possible current owner, so a later
        // transfer is legal too; only requests that loaded a stale version lose.
        Assert.All(outcomes, outcome => Assert.True(
            outcome.StatusCode == HttpStatusCode.OK
                || (outcome.StatusCode == HttpStatusCode.Conflict && outcome.Code == "conflict.concurrent_update"),
            $"Transfer to {outcome.Buyer} answered {(int)outcome.StatusCode} {outcome.Code}."));

        var owner = (await _client.GetFromJsonAsync<VehicleDetails>(Url($"/api/vehicles/{vehicle.Id}")))!.CustomerId;
        Assert.Contains(owner, outcomes.Where(outcome => outcome.StatusCode == HttpStatusCode.OK).Select(outcome => outcome.Buyer));

        var holders = 0;
        foreach (var customerId in buyers.Select(buyer => buyer.Id).Append(seller.Id))
        {
            var vehicles = await _client.GetFromJsonAsync<List<VehicleDetails>>(Url($"/api/customers/{customerId}/vehicles"));
            holders += vehicles!.Count;
        }

        Assert.Equal(1, holders);
    }

    public void Dispose() => _client.Dispose();

    private async Task<VehicleDetails> RegisterVehicleAsync(Guid customerId)
    {
        using var response = await _client.PostAsJsonAsync(
            Url($"/api/customers/{customerId}/vehicles"),
            new { vin = UniqueVin(), licensePlate = "B-JB 42", make = "BMW", model = "320d", modelYear = 2021, mileageKm = 30_000 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetails>())!;
    }
}
