using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Common;
using AutoFlow.Application.Invoices;
using AutoFlow.Application.WorkOrders;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Workflows;

/// <summary>
/// The end of a job told only through HTTP: the finished work order is
/// billed, the customer gets the PDF, pays a deposit at the counter and the
/// rest by bank transfer, and the invoice disappears from the open items.
/// </summary>
[Collection(ApiDatabaseDefinition.Name)]
public sealed class InvoiceWorkflowTests : IDisposable
{
    private readonly HttpClient _client;

    public InvoiceWorkflowTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _client = api.Factory.CreateClient();
    }

    [Fact]
    public async Task A_completed_job_is_invoiced_paid_in_two_steps_and_settled()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        var workOrder = await _client.CompleteWorkOrderAsync(vehicle.Id);

        // 1. The advisor bills the job.
        using var issued = await _client.PostAsync(Url($"/api/work-orders/{workOrder.Id}/invoice"), content: null);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var invoiceUrl = issued.Headers.Location!;
        var invoice = (await issued.Content.ReadFromJsonAsync<InvoiceDetails>())!;

        // 2. The work order is closed for good: no more lines.
        using var lateLine = await _client.PostAsJsonAsync(
            Url($"/api/work-orders/{workOrder.Id}/labour-lines"),
            new { description = "Forgotten test drive", hours = 0.25m, hourlyRate = 95m });
        await AssertProblemAsync(lateLine, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");

        // 3. The customer takes the PDF home and pays a deposit at the counter.
        using var pdf = await _client.GetAsync(At(invoiceUrl, "pdf"));
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        using var deposit = await _client.PostAsJsonAsync(At(invoiceUrl, "payments"), new { amount = 50m, method = "Cash" });
        Assert.Equal("PartiallyPaid", (await deposit.Content.ReadFromJsonAsync<InvoiceDetails>())!.Status);

        // 4. Still an open item for this customer.
        var openItems = await _client.GetFromJsonAsync<PagedList<InvoiceDetails>>(
            Url($"/api/invoices?customerId={owner.Id}&status=PartiallyPaid"));
        Assert.Equal(invoice.Id, Assert.Single(openItems!.Items).Id);

        // 5. The bank transfer of the rest settles it.
        using var transfer = await _client.PostAsJsonAsync(
            At(invoiceUrl, "payments"),
            new { amount = invoice.GrossTotal - 50m, method = "BankTransfer" });
        var settled = (await transfer.Content.ReadFromJsonAsync<InvoiceDetails>())!;
        Assert.Equal(("Paid", 0m), (settled.Status, settled.Balance));
        Assert.Equal(2, settled.Payments.Count);

        // 6. Nothing more can be paid, and the work order shows as invoiced.
        using var extra = await _client.PostAsJsonAsync(At(invoiceUrl, "payments"), new { amount = 1m, method = "Cash" });
        await AssertProblemAsync(extra, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        var closed = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{workOrder.Id}"));
        Assert.Equal("Invoiced", closed!.Status);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>A sub-path of a URL taken from a <c>Location</c> header (which may be absolute).</summary>
    private static Uri At(Uri resource, string segment) =>
        new($"{resource.OriginalString.TrimEnd('/')}/{segment}", UriKind.RelativeOrAbsolute);
}
