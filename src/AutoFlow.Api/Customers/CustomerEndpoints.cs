using AutoFlow.Api.Errors;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Domain.Customers;

namespace AutoFlow.Api.Customers;

/// <summary>Request body for creating and for replacing a customer.</summary>
public sealed record CustomerRequest(string? FirstName, string? LastName, string? Email, string? PhoneNumber)
    : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireText("firstName", FirstName, Customer.NameMaxLength);
        errors.RequireText("lastName", LastName, Customer.NameMaxLength);
        errors.RequireText("email", Email, EmailAddress.MaxLength);
        errors.OptionalText("phoneNumber", PhoneNumber, Customer.PhoneMaxLength);
    }
}

/// <summary>
/// <c>/api/customers</c>: list and search, read, create, replace and delete
/// customers. Endpoints only translate HTTP to use cases and results back to
/// HTTP; rules live in the application and domain layers.
/// </summary>
internal static class CustomerEndpoints
{
    private const string GetCustomerRoute = "GetCustomer";

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/api/customers").WithTags("Customers").WithRequestValidation();

        customers.MapGet("/", ListCustomers)
            .WithName("ListCustomers")
            .WithSummary("Lists customers page by page, optionally filtered by a search text.")
            .Produces<PagedList<CustomerDetails>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        customers.MapGet("/{id:guid}", GetCustomer)
            .WithName(GetCustomerRoute)
            .WithSummary("Gets a customer.")
            .Produces<CustomerDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        customers.MapPost("/", CreateCustomer)
            .WithName("CreateCustomer")
            .WithSummary("Registers a new customer.")
            .Produces<CustomerDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        customers.MapPut("/{id:guid}", UpdateCustomer)
            .WithName("UpdateCustomer")
            .WithSummary("Replaces a customer's name and contact details.")
            .Produces<CustomerDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        customers.MapDelete("/{id:guid}", DeleteCustomer)
            .WithName("DeleteCustomer")
            .WithSummary("Deletes a customer who owns no vehicles.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListCustomers(
        string? search,
        int? page,
        int? pageSize,
        ListCustomersHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ListCustomersQuery(search, page ?? 1, pageSize ?? Paging.DefaultPageSize),
            cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> GetCustomer(
        Guid id,
        GetCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCustomerQuery(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> CreateCustomer(
        CustomerRequest request,
        RegisterCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RegisterCustomerCommand(request.FirstName!, request.LastName!, request.Email!, request.PhoneNumber),
            cancellationToken);

        return result.Match(
            customer => Results.CreatedAtRoute(GetCustomerRoute, new { id = customer.Id }, customer),
            error => error.ToProblem());
    }

    private static async Task<IResult> UpdateCustomer(
        Guid id,
        CustomerRequest request,
        UpdateCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new UpdateCustomerCommand(id, request.FirstName!, request.LastName!, request.Email!, request.PhoneNumber),
            cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> DeleteCustomer(
        Guid id,
        DeleteCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteCustomerCommand(id), cancellationToken);

        return result.Match(_ => Results.NoContent(), error => error.ToProblem());
    }
}
