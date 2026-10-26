using AutoFlow.Api.Errors;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Api.Vehicles;

/// <summary>
/// Request body for registering a vehicle for a customer. Numbers are nullable
/// so that a missing field is reported as missing instead of arriving as 0.
/// </summary>
public sealed record VehicleRequest(
    string? Vin,
    string? LicensePlate,
    string? Make,
    string? Model,
    int? ModelYear,
    int? MileageKm) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // VIN and plate formats are normalized and checked by the domain (422).
        errors.RequireText("vin", Vin, maxLength: 64);
        errors.RequireText("licensePlate", LicensePlate, maxLength: 64);
        errors.RequireText("make", Make, Vehicle.MakeModelMaxLength);
        errors.RequireText("model", Model, Vehicle.MakeModelMaxLength);

        // The upper bound depends on today's date, so the domain checks it.
        errors.RequireAtLeast("modelYear", ModelYear, Vehicle.EarliestModelYear);
        errors.RequireAtLeast("mileageKm", MileageKm, 0);
    }
}

/// <summary>Request body for recording an odometer reading.</summary>
public sealed record MileageRequest(int? MileageKm) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireAtLeast("mileageKm", MileageKm, 0);
    }
}

/// <summary>Request body for transferring a vehicle to another customer.</summary>
public sealed record TransferVehicleRequest(Guid? CustomerId) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireId("customerId", CustomerId);
    }
}

/// <summary>
/// Vehicles as a sub-resource of their owner
/// (<c>/api/customers/{customerId}/vehicles</c>: list, register) and as a
/// resource of their own (<c>/api/vehicles/{id}</c>: read, record mileage,
/// transfer). A vehicle keeps its URL when it changes hands.
/// </summary>
internal static class VehicleEndpoints
{
    private const string GetVehicleRoute = "GetVehicle";

    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var owned = app.MapGroup("/api/customers/{customerId:guid}/vehicles")
            .WithTags("Vehicles")
            .WithRequestValidation();

        owned.MapGet("/", ListCustomerVehicles)
            .WithName("ListCustomerVehicles")
            .WithSummary("Lists the vehicles a customer owns, oldest registration first.")
            .Produces<IReadOnlyList<VehicleDetails>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        owned.MapPost("/", RegisterVehicle)
            .WithName("RegisterVehicle")
            .WithSummary("Registers a vehicle for a customer.")
            .Produces<VehicleDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var vehicles = app.MapGroup("/api/vehicles").WithTags("Vehicles").WithRequestValidation();

        vehicles.MapGet("/{id:guid}", GetVehicle)
            .WithName(GetVehicleRoute)
            .WithSummary("Gets a vehicle.")
            .Produces<VehicleDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        vehicles.MapPut("/{id:guid}/mileage", RecordMileage)
            .WithName("RecordMileage")
            .WithSummary("Records an odometer reading; readings never go backwards.")
            .Produces<VehicleDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        vehicles.MapPost("/{id:guid}/transfer", TransferVehicle)
            .WithName("TransferVehicle")
            .WithSummary("Transfers a vehicle to another existing customer.")
            .Produces<VehicleDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListCustomerVehicles(
        Guid customerId,
        ListCustomerVehiclesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListCustomerVehiclesQuery(customerId), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> RegisterVehicle(
        Guid customerId,
        VehicleRequest request,
        RegisterVehicleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RegisterVehicleCommand(
                customerId,
                request.Vin!,
                request.LicensePlate!,
                request.Make!,
                request.Model!,
                request.ModelYear!.Value,
                request.MileageKm!.Value),
            cancellationToken);

        return result.Match(
            vehicle => Results.CreatedAtRoute(GetVehicleRoute, new { id = vehicle.Id }, vehicle),
            error => error.ToProblem());
    }

    private static async Task<IResult> GetVehicle(
        Guid id,
        GetVehicleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetVehicleQuery(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> RecordMileage(
        Guid id,
        MileageRequest request,
        RecordMileageHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RecordMileageCommand(id, request.MileageKm!.Value), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> TransferVehicle(
        Guid id,
        TransferVehicleRequest request,
        TransferVehicleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new TransferVehicleCommand(id, request.CustomerId!.Value), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }
}
