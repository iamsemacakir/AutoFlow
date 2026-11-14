using AutoFlow.Api.Errors;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Bays;
using AutoFlow.Domain.Bays;

namespace AutoFlow.Api.Bays;

/// <summary>Request body for creating a bay and for renaming one.</summary>
public sealed record BayRequest(string? Name) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireText("name", Name, Bay.NameMaxLength);
    }
}

/// <summary>
/// <c>/api/bays</c>: the workshop's capacity. Bays are listed (all of them,
/// by name, unpaged), read, created and renamed; going out of service and
/// back are actions of their own (<c>/take-out-of-service</c>,
/// <c>/return-to-service</c>) because they are not a plain replacement and
/// fail from the wrong state (422). Bays are never deleted, so there is no
/// <c>DELETE</c>.
/// </summary>
internal static class BayEndpoints
{
    private const string Tag = "Bays";
    private const string GetBayRoute = "GetBay";

    public static IEndpointRouteBuilder MapBayEndpoints(this IEndpointRouteBuilder app)
    {
        var bays = app.MapGroup("/api/bays").WithTags(Tag).WithRequestValidation();

        bays.MapGet("/", ListBays)
            .WithName("ListBays")
            .WithSummary("Lists every bay, in and out of service, by name.")
            .Produces<IReadOnlyList<BayDetails>>();

        bays.MapGet("/{id:guid}", GetBay)
            .WithName(GetBayRoute)
            .WithSummary("Gets a bay.")
            .Produces<BayDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        bays.MapPost("/", CreateBay)
            .WithName("CreateBay")
            .WithSummary("Adds a bay, in service, with a name no other bay has.")
            .Produces<BayDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        bays.MapPut("/{id:guid}", RenameBay)
            .WithName("RenameBay")
            .WithSummary("Renames a bay; the name must not be in use by another bay.")
            .Produces<BayDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        MapAction(bays, "/{id:guid}/take-out-of-service", TakeBayOutOfService)
            .WithName("TakeBayOutOfService")
            .WithSummary("Stops new bookings for a bay; appointments already booked stay until moved.");

        MapAction(bays, "/{id:guid}/return-to-service", ReturnBayToService)
            .WithName("ReturnBayToService")
            .WithSummary("Puts a bay back in service so it can be booked again.");

        return app;
    }

    private static async Task<IResult> ListBays(ListBaysHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(new ListBaysQuery(), cancellationToken));

    private static async Task<IResult> GetBay(Guid id, GetBayHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetBayQuery(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> CreateBay(
        BayRequest request,
        CreateBayHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateBayCommand(request.Name!), cancellationToken);

        return result.Match(
            bay => Results.CreatedAtRoute(GetBayRoute, new { id = bay.Id }, bay),
            error => error.ToProblem());
    }

    private static async Task<IResult> RenameBay(
        Guid id,
        BayRequest request,
        RenameBayHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RenameBayCommand(id, request.Name!), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    /// <summary>Both service actions answer the same way: the updated bay, or why it was refused.</summary>
    private static RouteHandlerBuilder MapAction(RouteGroupBuilder group, string pattern, Delegate handler) =>
        group.MapPost(pattern, handler)
            .Produces<BayDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> TakeBayOutOfService(
        Guid id,
        TakeBayOutOfServiceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new TakeBayOutOfServiceCommand(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> ReturnBayToService(
        Guid id,
        ReturnBayToServiceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ReturnBayToServiceCommand(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }
}
