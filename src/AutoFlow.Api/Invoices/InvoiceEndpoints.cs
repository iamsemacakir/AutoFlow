using AutoFlow.Api.Auth;
using AutoFlow.Api.Concurrency;
using AutoFlow.Api.Errors;
using AutoFlow.Api.Observability;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Common;
using AutoFlow.Application.Invoices;
using AutoFlow.Domain.Invoices;

namespace AutoFlow.Api.Invoices;

/// <summary>
/// Request body for recording money received. <c>method</c> is <c>Cash</c>,
/// <c>Card</c> or <c>BankTransfer</c>; <c>receivedAt</c> defaults to now (a
/// bank transfer is often entered days after it arrived).
/// </summary>
public sealed record RecordPaymentRequest(decimal? Amount, string? Method, DateTimeOffset? ReceivedAt)
    : IValidatableRequest
{
    /// <summary>Longest payment method name (<c>BankTransfer</c>), with room to spare.</summary>
    private const int MethodMaxLength = 20;

    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // Positive whole cents within the balance is a domain rule (422).
        errors.RequireNumber("amount", Amount);
        errors.RequireText("method", Method, MethodMaxLength);
    }
}

/// <summary>
/// Invoices are issued for a work order (<c>POST /api/work-orders/{id}/invoice</c>,
/// singular: there is at most one) and addressed on their own afterwards
/// (<c>/api/invoices/{id}</c>). An invoice never changes once issued; the
/// only thing that happens to it is a payment, which answers with the whole
/// invoice and its new balance. <c>/pdf</c> downloads it as a document.
/// See ADR 0018 to ADR 0020.
/// </summary>
internal static class InvoiceEndpoints
{
    private const string Tag = "Invoices";
    private const string GetInvoiceRoute = "GetInvoice";

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var ofWorkOrder = app.MapGroup("/api/work-orders/{workOrderId:guid}/invoice")
            .WithTags(Tag)
            .WithRequestValidation();

        ofWorkOrder.MapPost("/", IssueInvoice)
            .WithName("IssueInvoice")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Issues the invoice for a completed work order with the next number of the year.")
            .Produces<InvoiceDetails>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        ofWorkOrder.MapGet("/", GetWorkOrderInvoice)
            .WithName("GetWorkOrderInvoice")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Gets the invoice issued for a work order.")
            .Produces<InvoiceDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var invoices = app.MapGroup("/api/invoices").WithTags(Tag).WithRequestValidation();

        invoices.MapGet("/", ListInvoices)
            .WithName("ListInvoices")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Lists invoices page by page, newest number first, optionally by status, customer and whether they are overdue.")
            .Produces<PagedList<InvoiceDetails>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        invoices.MapGet("/{id:guid}", GetInvoice)
            .WithRowVersion<Invoice>(readOnly: true)
            .WithName(GetInvoiceRoute)
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Gets an invoice with its lines, totals and payments.")
            .Produces<InvoiceDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        invoices.MapGet("/{id:guid}/pdf", ExportInvoice)
            .WithName("ExportInvoicePdf")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Downloads the invoice as a PDF, with the payments received so far.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound);

        invoices.MapPost("/{id:guid}/payments", RecordPayment)
            .WithRowVersion<Invoice>()
            .WithName("RecordPayment")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Records money received; part payments are fine, more than the balance is not.")
            .Produces<InvoiceDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> IssueInvoice(
        Guid workOrderId,
        IssueInvoiceHandler handler,
        AutoFlowMetrics metrics,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new IssueInvoiceCommand(workOrderId), cancellationToken);

        return result.Match(
            invoice =>
            {
                metrics.InvoiceIssued(invoice.GrossTotal);
                return Results.CreatedAtRoute(GetInvoiceRoute, new { id = invoice.Id }, invoice);
            },
            error => error.ToProblem());
    }

    private static async Task<IResult> GetWorkOrderInvoice(
        Guid workOrderId,
        GetWorkOrderInvoiceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetWorkOrderInvoiceQuery(workOrderId), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> ListInvoices(
        string? status,
        Guid? customerId,
        bool? overdue,
        int? page,
        int? pageSize,
        ListInvoicesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ListInvoicesQuery(status, customerId, overdue, page ?? 1, pageSize ?? Paging.DefaultPageSize),
            cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> GetInvoice(Guid id, GetInvoiceHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetInvoiceQuery(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> ExportInvoice(
        Guid id,
        ExportInvoiceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ExportInvoiceQuery(id), cancellationToken);

        return result.Match(
            document => Results.File(document.Content.ToArray(), document.ContentType, document.FileName),
            error => error.ToProblem());
    }

    private static async Task<IResult> RecordPayment(
        Guid id,
        RecordPaymentRequest request,
        RecordPaymentHandler handler,
        AutoFlowMetrics metrics,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RecordPaymentCommand(id, request.Amount!.Value, request.Method!, request.ReceivedAt),
            cancellationToken);

        return result.Match(
            value =>
            {
                metrics.PaymentRecorded(
                    Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method) ? method.ToString() : "Other");
                return Results.Ok(value);
            },
            error => error.ToProblem());
    }
}
