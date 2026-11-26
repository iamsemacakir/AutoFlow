using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AutoFlow.Application.Common;
using AutoFlow.Application.Invoices;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Invoices;

[Collection(ApiDatabaseDefinition.Name)]
public sealed class InvoiceEndpointsTests : IDisposable
{
    private readonly ApiDatabaseFixture _api;
    private readonly HttpClient _client;

    public InvoiceEndpointsTests(ApiDatabaseFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _client = api.CreateClient();
    }

    [Fact]
    public async Task Issue_answers_201_with_the_invoice_and_marks_the_work_order_invoiced()
    {
        var (workOrder, vehicle) = await CompletedWorkOrderAsync();

        using var response = await _client.PostAsync(Url($"/api/work-orders/{workOrder.Id}/invoice"), content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invoice = await response.Content.ReadFromJsonAsync<InvoiceDetails>();
        Assert.NotNull(invoice);
        Assert.Matches($"^{DateTime.UtcNow.Year}-\\d{{5}}$", invoice.Number);
        Assert.Equal((workOrder.Id, vehicle.CustomerId), (invoice.WorkOrderId, invoice.CustomerId));
        Assert.Equal("Anna Schmidt", invoice.CustomerName);
        Assert.Contains(vehicle.Vin, invoice.VehicleDescription, StringComparison.Ordinal);
        Assert.Equal("Issued", invoice.Status);
        Assert.Equal((19m, 142.5m, 27.08m, 169.58m), (invoice.VatRatePercent, invoice.NetTotal, invoice.VatAmount, invoice.GrossTotal));
        Assert.Equal(169.58m, invoice.Balance);
        Assert.Equal(DateOnly.FromDateTime(invoice.IssuedAt.UtcDateTime).AddDays(14), invoice.DueDate);
        Assert.False(invoice.IsOverdue);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((1, "Labour", 142.5m), (line.Number, line.Kind, line.Total));
        Assert.EndsWith($"/api/invoices/{invoice.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var loaded = await _client.GetFromJsonAsync<InvoiceDetails>(response.Headers.Location);
        Assert.Equal(invoice.Number, loaded!.Number);
        var invoiced = await _client.GetFromJsonAsync<WorkOrderDetails>(Url($"/api/work-orders/{workOrder.Id}"));
        Assert.Equal("Invoiced", invoiced!.Status);
        Assert.NotNull(invoiced.InvoicedAt);
    }

    [Fact]
    public async Task Invoices_issued_one_after_another_get_consecutive_numbers()
    {
        var (first, _) = await CompletedWorkOrderAsync();
        var (second, _) = await CompletedWorkOrderAsync();

        var a = await _client.IssueInvoiceAsync(first.Id);
        var b = await _client.IssueInvoiceAsync(second.Id);

        Assert.Equal(SequenceOf(a.Number) + 1, SequenceOf(b.Number));
    }

    [Fact]
    public async Task Issue_twice_returns_422_and_keeps_the_first_invoice()
    {
        var (workOrder, _) = await CompletedWorkOrderAsync();
        var first = await _client.IssueInvoiceAsync(workOrder.Id);

        using var second = await _client.PostAsync(Url($"/api/work-orders/{workOrder.Id}/invoice"), content: null);

        await AssertProblemAsync(second, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        var invoice = await _client.GetFromJsonAsync<InvoiceDetails>(Url($"/api/work-orders/{workOrder.Id}/invoice"));
        Assert.Equal(first.Id, invoice!.Id);
    }

    [Fact]
    public async Task Issue_for_a_work_order_that_is_not_completed_returns_422()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        var draft = await _client.OpenWorkOrderAsync(vehicle.Id);

        using var response = await _client.PostAsync(Url($"/api/work-orders/{draft.Id}/invoice"), content: null);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
    }

    [Fact]
    public async Task Issue_and_get_for_an_unknown_work_order_return_404()
    {
        var id = Guid.NewGuid();

        using var issued = await _client.PostAsync(Url($"/api/work-orders/{id}/invoice"), content: null);
        using var read = await _client.GetAsync(Url($"/api/work-orders/{id}/invoice"));

        await AssertProblemAsync(issued, HttpStatusCode.NotFound, "work_order.not_found");
        await AssertProblemAsync(read, HttpStatusCode.NotFound, "work_order.not_found");
    }

    [Fact]
    public async Task Get_for_a_work_order_without_invoice_returns_404_not_issued()
    {
        var (workOrder, _) = await CompletedWorkOrderAsync();

        using var response = await _client.GetAsync(Url($"/api/work-orders/{workOrder.Id}/invoice"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "invoice.not_issued");
    }

    [Fact]
    public async Task Get_of_an_unknown_invoice_returns_404()
    {
        using var response = await _client.GetAsync(Url($"/api/invoices/{Guid.NewGuid()}"));
        using var pdf = await _client.GetAsync(Url($"/api/invoices/{Guid.NewGuid()}/pdf"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "invoice.not_found");
        await AssertProblemAsync(pdf, HttpStatusCode.NotFound, "invoice.not_found");
    }

    [Fact]
    public async Task Payments_reduce_the_balance_until_the_invoice_is_paid()
    {
        var invoice = await IssuedInvoiceAsync();

        using var part = await PayAsync(invoice.Id, new { amount = 100m, method = "Cash" });
        var afterPart = await part.Content.ReadFromJsonAsync<InvoiceDetails>();
        Assert.Equal(HttpStatusCode.OK, part.StatusCode);
        Assert.Equal(("PartiallyPaid", 100m, 69.58m), (afterPart!.Status, afterPart.AmountPaid, afterPart.Balance));

        var receivedAt = invoice.IssuedAt.AddMinutes(1).ToOffset(TimeSpan.FromHours(2));
        using var rest = await PayAsync(invoice.Id, new { amount = 69.58m, method = "BankTransfer", receivedAt });
        var paid = await rest.Content.ReadFromJsonAsync<InvoiceDetails>();

        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        Assert.Equal(("Paid", 169.58m, 0m), (paid!.Status, paid.AmountPaid, paid.Balance));
        Assert.Equal(["Cash", "BankTransfer"], paid.Payments.OrderByDescending(p => p.Amount).Select(p => p.Method).ToArray());
        AssertSameInstant(receivedAt, paid.PaidAt!.Value);
    }

    [Fact]
    public async Task Payment_above_the_balance_returns_422_and_records_nothing()
    {
        var invoice = await IssuedInvoiceAsync();

        using var response = await PayAsync(invoice.Id, new { amount = 169.59m, method = "Card" });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "domain.rule_violated");
        var loaded = await _client.GetFromJsonAsync<InvoiceDetails>(Url($"/api/invoices/{invoice.Id}"));
        Assert.Empty(loaded!.Payments);
    }

    [Fact]
    public async Task Payment_with_an_unknown_method_returns_400()
    {
        var invoice = await IssuedInvoiceAsync();

        using var response = await PayAsync(invoice.Id, new { amount = 10m, method = "Cheque" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invoice.unknown_payment_method");
    }

    [Fact]
    public async Task Payment_without_amount_and_method_returns_400()
    {
        var invoice = await IssuedInvoiceAsync();

        using var response = await PayAsync(invoice.Id, new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Pdf_downloads_the_invoice_as_a_named_pdf_file()
    {
        var invoice = await IssuedInvoiceAsync();

        using var response = await _client.GetAsync(Url($"/api/invoices/{invoice.Id}/pdf"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"invoice-{invoice.Number}.pdf", response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Contains($"({invoice.Number}) Tj", System.Text.Encoding.ASCII.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_filters_by_customer_and_status_newest_first()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        var older = await _client.IssueInvoiceAsync((await _client.CompleteWorkOrderAsync(vehicle.Id)).Id);
        var newer = await _client.IssueInvoiceAsync((await _client.CompleteWorkOrderAsync(vehicle.Id)).Id);
        (await PayAsync(older.Id, new { amount = older.GrossTotal, method = "Card" })).Dispose();

        var all = await _client.GetFromJsonAsync<PagedList<InvoiceDetails>>(Url($"/api/invoices?customerId={owner.Id}"));
        var open = await _client.GetFromJsonAsync<PagedList<InvoiceDetails>>(Url($"/api/invoices?customerId={owner.Id}&status=Issued"));
        var paid = await _client.GetFromJsonAsync<PagedList<InvoiceDetails>>(Url($"/api/invoices?customerId={owner.Id}&status=paid"));

        Assert.Equal([newer.Id, older.Id], all!.Items.Select(i => i.Id).ToArray());
        Assert.Equal(newer.Id, Assert.Single(open!.Items).Id);
        Assert.Equal(older.Id, Assert.Single(paid!.Items).Id);
    }

    [Fact]
    public async Task List_by_overdue_keeps_unpaid_invoices_once_their_due_date_has_passed()
    {
        var clock = new ShiftedTimeProvider();
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = _api.AuthorizationFor(UserRole.Owner);
        var owner = await client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await client.RegisterVehicleAsync(owner.Id);
        var open = await client.IssueInvoiceAsync((await client.CompleteWorkOrderAsync(vehicle.Id)).Id);
        var paid = await client.IssueInvoiceAsync((await client.CompleteWorkOrderAsync(vehicle.Id)).Id);
        (await client.PostAsJsonAsync(
            Url($"/api/invoices/{paid.Id}/payments"),
            new { amount = paid.GrossTotal, method = "Card" })).Dispose();
        var url = $"/api/invoices?customerId={owner.Id}&overdue=";

        var notYet = await client.GetFromJsonAsync<PagedList<InvoiceDetails>>(Url(url + "true"));
        // The payment term is 14 days; 15 days later the unpaid invoice is overdue.
        clock.Shift = TimeSpan.FromDays(15);
        var overdue = await client.GetFromJsonAsync<PagedList<InvoiceDetails>>(Url(url + "true"));
        var notOverdue = await client.GetFromJsonAsync<PagedList<InvoiceDetails>>(Url(url + "false"));

        Assert.Equal(0, notYet!.TotalCount);
        var chased = Assert.Single(overdue!.Items);
        Assert.Equal(open.Id, chased.Id);
        Assert.True(chased.IsOverdue);
        Assert.Equal(paid.Id, Assert.Single(notOverdue!.Items).Id);
    }

    [Theory]
    [InlineData("/api/invoices?status=Overdue", "invoice.unknown_status")]
    [InlineData("/api/invoices?pageSize=101", "paging.invalid_page_size")]
    [InlineData("/api/invoices?overdue=maybe", "request.malformed")]
    public async Task List_with_invalid_filters_returns_400(string url, string code)
    {
        using var response = await _client.GetAsync(Url(url));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    public void Dispose() => _client.Dispose();

    private static int SequenceOf(string number) => int.Parse(number[5..], CultureInfo.InvariantCulture);

    private async Task<(WorkOrderDetails WorkOrder, VehicleDetails Vehicle)> CompletedWorkOrderAsync()
    {
        var owner = await _client.CreateCustomerAsync("Anna", "Schmidt");
        var vehicle = await _client.RegisterVehicleAsync(owner.Id);
        return (await _client.CompleteWorkOrderAsync(vehicle.Id), vehicle);
    }

    private async Task<InvoiceDetails> IssuedInvoiceAsync()
    {
        var (workOrder, _) = await CompletedWorkOrderAsync();
        return await _client.IssueInvoiceAsync(workOrder.Id);
    }

    private Task<HttpResponseMessage> PayAsync(Guid invoiceId, object body) =>
        _client.PostAsJsonAsync(Url($"/api/invoices/{invoiceId}/payments"), body);
}
