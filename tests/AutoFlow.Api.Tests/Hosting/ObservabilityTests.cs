using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using AutoFlow.Api.Observability;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Hosting;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class ObservabilityTests(ApiDatabaseFixture api)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer_without_a_token(string path)
    {
        using var anonymous = api.Factory.CreateClient();

        using var response = await anonymous.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Issuing_an_invoice_and_taking_a_payment_are_counted()
    {
        var measurements = new ConcurrentBag<(string Instrument, double Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == AutoFlowMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => measurements.Add((instrument.Name, value)));
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => measurements.Add((instrument.Name, value)));
        listener.Start();

        using var client = api.CreateClient();
        var owner = await client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await client.RegisterVehicleAsync(owner.Id);
        var workOrder = await client.CompleteWorkOrderAsync(vehicle.Id);
        var invoice = await client.IssueInvoiceAsync(workOrder.Id);
        using var payment = await client.PostAsync(
            Url($"/api/invoices/{invoice.Id}/payments"),
            System.Net.Http.Json.JsonContent.Create(new { amount = 10m, method = "cash" }));
        payment.EnsureSuccessStatusCode();

        Assert.Contains(measurements, m => m.Instrument == "autoflow.work_orders.opened");
        Assert.Contains(measurements, m => m.Instrument == "autoflow.invoices.issued");
        Assert.Contains(measurements, m => m.Instrument == "autoflow.invoices.gross_amount" && m.Value == (double)invoice.GrossTotal);
        Assert.Contains(measurements, m => m.Instrument == "autoflow.payments.recorded");
    }
}
