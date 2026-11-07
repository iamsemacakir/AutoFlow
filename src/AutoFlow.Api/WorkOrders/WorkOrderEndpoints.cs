using AutoFlow.Api.Errors;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Common;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Api.WorkOrders;

/// <summary>
/// Request body for opening a work order at intake. The odometer reading is
/// optional; when it is given it is also recorded on the vehicle.
/// </summary>
public sealed record OpenWorkOrderRequest(string? Complaint, int? MileageKm) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireText("complaint", Complaint, WorkOrder.ComplaintMaxLength);

        // A reading below the vehicle's last one is a business rule (422).
        errors.OptionalAtLeast("mileageKm", MileageKm, 0);
    }
}

/// <summary>
/// Work orders as a sub-resource of the vehicle they are for
/// (<c>/api/vehicles/{vehicleId}/work-orders</c>: service history, open) and
/// as a resource of their own (<c>/api/work-orders/{id}</c>). A work order
/// stays at its URL for good, whatever happens to the vehicle. See ADR 0012.
/// </summary>
internal static class WorkOrderEndpoints
{
    private const string Tag = "Work orders";
    private const string GetWorkOrderRoute = "GetWorkOrder";

    public static IEndpointRouteBuilder MapWorkOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var history = app.MapGroup("/api/vehicles/{vehicleId:guid}/work-orders")
            .WithTags(Tag)
            .WithRequestValidation();

        history.MapGet("/", ListVehicleWorkOrders)
            .WithName("ListVehicleWorkOrders")
            .WithSummary("Lists a vehicle's work orders page by page, newest first, across all owners.")
            .Produces<PagedList<WorkOrderDetails>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        history.MapPost("/", OpenWorkOrder)
            .WithName("OpenWorkOrder")
            .WithSummary("Opens a draft work order for the vehicle's current owner at intake.")
            .Produces<WorkOrderDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var workOrders = app.MapGroup("/api/work-orders").WithTags(Tag).WithRequestValidation();

        workOrders.MapGet("/{id:guid}", GetWorkOrder)
            .WithName(GetWorkOrderRoute)
            .WithSummary("Gets a work order with its lines and totals.")
            .Produces<WorkOrderDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListVehicleWorkOrders(
        Guid vehicleId,
        int? page,
        int? pageSize,
        ListVehicleWorkOrdersHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ListVehicleWorkOrdersQuery(vehicleId, page ?? 1, pageSize ?? Paging.DefaultPageSize),
            cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> OpenWorkOrder(
        Guid vehicleId,
        OpenWorkOrderRequest request,
        OpenWorkOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new OpenWorkOrderCommand(vehicleId, request.Complaint!, request.MileageKm),
            cancellationToken);

        return result.Match(
            workOrder => Results.CreatedAtRoute(GetWorkOrderRoute, new { id = workOrder.Id }, workOrder),
            error => error.ToProblem());
    }

    private static async Task<IResult> GetWorkOrder(
        Guid id,
        GetWorkOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetWorkOrderQuery(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }
}
