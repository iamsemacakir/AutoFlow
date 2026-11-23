using System.Net;
using System.Net.Http.Json;
using AutoFlow.Domain.Users;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Auth;

/// <summary>
/// The access matrix (<see cref="AuthorizationMatrixTests"/>) as each role
/// experiences it: a day at the front desk and on the workshop floor.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class RoleAccessTests : IDisposable
{
    private readonly ApiDatabaseFixture _api;
    private readonly HttpClient _owner;
    private readonly HttpClient _advisor;
    private readonly HttpClient _mechanic;

    public RoleAccessTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _owner = api.CreateClient(UserRole.Owner);
        _advisor = api.CreateClient(UserRole.ServiceAdvisor);
        _mechanic = api.CreateClient(UserRole.Mechanic);
    }

    [Fact]
    public async Task Mechanic_reads_the_plan_and_works_on_work_orders_but_does_not_sell()
    {
        var owner = await _advisor.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _advisor.RegisterVehicleAsync(owner.Id);
        var workOrder = await _advisor.OpenWorkOrderAsync(vehicle.Id);

        using var customers = await _mechanic.GetAsync(Url("/api/customers"));
        using var schedule = await _mechanic.GetAsync(Url("/api/schedule?date=2026-11-20"));
        using var workOrders = await _mechanic.GetAsync(Url("/api/work-orders?status=Draft"));
        using var labour = await _mechanic.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/labour-lines"),
            new { description = "Diagnosis", hours = 0.5m, hourlyRate = 95m });
        using var mileage = await _mechanic.PutAsJsonAsync(Url($"/api/vehicles/{vehicle.Id}/mileage"), new { mileageKm = 31_000 });

        using var approve = await _mechanic.PostAsync(Url($"/api/work-orders/{workOrder.Id}/approve"), content: null);
        using var newCustomer = await _mechanic.PostAsJsonAsync(
            Url("/api/customers"),
            new { firstName = "Mia", lastName = "Wagner", email = "mia@example.com" });
        using var invoices = await _mechanic.GetAsync(Url("/api/invoices"));
        using var newBay = await _mechanic.PostAsJsonAsync(Url("/api/bays"), new { name = UniqueBayName() });

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK],
            new[] { customers.StatusCode, schedule.StatusCode, workOrders.StatusCode, labour.StatusCode, mileage.StatusCode });
        await AssertProblemAsync(approve, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertProblemAsync(newCustomer, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertProblemAsync(invoices, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertProblemAsync(newBay, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task Service_advisor_runs_the_front_desk_but_not_the_workshop_settings()
    {
        var owner = await _advisor.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _advisor.RegisterVehicleAsync(owner.Id);
        var workOrder = await _advisor.CompleteWorkOrderAsync(vehicle.Id);

        using var invoice = await _advisor.PostAsync(Url($"/api/work-orders/{workOrder.Id}/invoice"), content: null);
        using var newBay = await _advisor.PostAsJsonAsync(Url("/api/bays"), new { name = UniqueBayName() });
        using var users = await _advisor.GetAsync(Url("/api/users"));

        Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
        await AssertProblemAsync(newBay, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertProblemAsync(users, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task Owner_may_do_everything()
    {
        using var newBay = await _owner.PostAsJsonAsync(Url("/api/bays"), new { name = UniqueBayName() });
        using var users = await _owner.GetAsync(Url("/api/users"));
        using var invoices = await _owner.GetAsync(Url("/api/invoices"));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK],
            new[] { newBay.StatusCode, users.StatusCode, invoices.StatusCode });
    }

    [Theory]
    [InlineData("/api/customers")]
    [InlineData("/api/schedule?date=2026-11-20")]
    [InlineData("/api/nothing-here")]
    public async Task Without_a_token_every_api_path_answers_401(string path)
    {
        using var anonymous = _api.Factory.CreateClient();

        using var response = await anonymous.GetAsync(Url(path));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/")]
    public async Task Health_check_and_api_info_stay_public(string path)
    {
        using var anonymous = _api.Factory.CreateClient();

        using var response = await anonymous.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _owner.Dispose();
        _advisor.Dispose();
        _mechanic.Dispose();
    }
}
