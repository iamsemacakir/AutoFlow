using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Invoices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Hosting;

/// <summary>
/// A fresh installation started the way <c>docker compose up</c> starts it:
/// migrations, initial owner and demo workshop on start, in a database of
/// its own.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class DemoDataTests(ApiDatabaseFixture api)
{
    private const string DemoPassword = "autoflow-demo";

    [Fact]
    public async Task Demo_workshop_is_seeded_into_an_empty_database_once()
    {
        var connectionString = api.ConnectionStringForNewDatabase();
        await using var root = new WebApplicationFactory<Program>();
        await using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AutoFlow", connectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Auth:InitialOwner:Email", "owner@autoflow.demo");
            builder.UseSetting("Auth:InitialOwner:Password", DemoPassword);
            builder.UseSetting("Seed:DemoData", "true");
            builder.UseSetting("Seed:TimeZone", "Europe/Berlin");
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await ApiDatabaseFixture.SignInAsync(client, "advisor@autoflow.demo", DemoPassword));

        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, berlin).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var schedule = await client.GetFromJsonAsync<DaySchedule>(Url($"/api/schedule?date={today}&timeZone=Europe/Berlin"));
        var customers = await client.GetFromJsonAsync<PagedList<CustomerDetails>>(Url("/api/customers"));
        var invoices = await client.GetFromJsonAsync<PagedList<InvoiceDetails>>(Url("/api/invoices"));

        Assert.Equal(["Alignment bay", "Lift 1", "Lift 2", "Lift 3"], schedule!.Bays.Select(b => b.Bay.Name).ToArray());
        Assert.False(schedule.Bays.Single(b => b.Bay.Name == "Lift 3").Bay.IsInService);
        Assert.Equal(
            ["CheckedIn", "Scheduled", "Scheduled"],
            schedule.Bays.Single(b => b.Bay.Name == "Lift 1").Appointments.Select(a => a.Status).ToArray());
        Assert.Equal(
            ["NoShow", "Scheduled"],
            schedule.Bays.Single(b => b.Bay.Name == "Lift 2").Appointments.Select(a => a.Status).ToArray());
        Assert.Equal(8, customers!.TotalCount);
        Assert.Equal(
            [("Paid", false), ("PartiallyPaid", false), ("Issued", true)],
            invoices!.Items.Select(i => (i.Status, i.IsOverdue)).ToArray());

        // A second start finds customers and leaves the data alone.
        await using var restarted = root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AutoFlow", connectionString);
            builder.UseSetting("Seed:DemoData", "true");
        });
        using var again = restarted.CreateClient();
        again.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var unchanged = await again.GetFromJsonAsync<PagedList<CustomerDetails>>(Url("/api/customers"));
        Assert.Equal(8, unchanged!.TotalCount);
    }
}
