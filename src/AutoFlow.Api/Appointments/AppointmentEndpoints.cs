using AutoFlow.Api.Errors;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Appointments;
using AutoFlow.Domain.Appointments;

namespace AutoFlow.Api.Appointments;

/// <summary>
/// Request body for booking a bay for a vehicle. <c>start</c> and <c>end</c>
/// are ISO 8601 timestamps with an offset (<c>Z</c> or <c>+01:00</c>); the
/// slot is stored in UTC.
/// </summary>
public sealed record BookAppointmentRequest(Guid? BayId, DateTimeOffset? Start, DateTimeOffset? End, string? Purpose)
    : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireId("bayId", BayId);

        // End after start, the 15-minute grid and "not in the past" are domain rules (422).
        errors.RequireTimestamp("start", Start);
        errors.RequireTimestamp("end", End);
        errors.RequireText("purpose", Purpose, Appointment.PurposeMaxLength);
    }
}

/// <summary>
/// Request body for moving a scheduled appointment: the target bay (the
/// current one to change only the time) and the new slot.
/// </summary>
public sealed record RescheduleAppointmentRequest(Guid? BayId, DateTimeOffset? Start, DateTimeOffset? End)
    : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireId("bayId", BayId);
        errors.RequireTimestamp("start", Start);
        errors.RequireTimestamp("end", End);
    }
}

/// <summary>
/// Appointments are booked under the vehicle that is brought in
/// (<c>POST /api/vehicles/{vehicleId}/appointments</c>) and addressed on
/// their own afterwards (<c>/api/appointments/{id}</c>), like work orders,
/// so the URL survives a sale of the car. Moving one is an action
/// (<c>/reschedule</c>) because it is checked against the other
/// appointments, not a plain replacement. A bay out of service or a clash
/// with another appointment answers 409 with a specific code; a slot off the
/// 15-minute grid or in the past answers 422.
/// </summary>
internal static class AppointmentEndpoints
{
    private const string Tag = "Appointments";
    private const string GetAppointmentRoute = "GetAppointment";

    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var byVehicle = app.MapGroup("/api/vehicles/{vehicleId:guid}/appointments")
            .WithTags(Tag)
            .WithRequestValidation();

        byVehicle.MapPost("/", BookAppointment)
            .WithName("BookAppointment")
            .WithSummary("Books a bay and slot for the vehicle, for its current owner.")
            .Produces<AppointmentDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var appointments = app.MapGroup("/api/appointments").WithTags(Tag).WithRequestValidation();

        appointments.MapGet("/{id:guid}", GetAppointment)
            .WithName(GetAppointmentRoute)
            .WithSummary("Gets an appointment, in any status.")
            .Produces<AppointmentDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        appointments.MapPost("/{id:guid}/reschedule", RescheduleAppointment)
            .WithName("RescheduleAppointment")
            .WithSummary("Moves a scheduled appointment to another slot, another bay, or both.")
            .Produces<AppointmentDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> BookAppointment(
        Guid vehicleId,
        BookAppointmentRequest request,
        BookAppointmentHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new BookAppointmentCommand(
                vehicleId,
                request.BayId!.Value,
                request.Start!.Value,
                request.End!.Value,
                request.Purpose!),
            cancellationToken);

        return result.Match(
            appointment => Results.CreatedAtRoute(GetAppointmentRoute, new { id = appointment.Id }, appointment),
            error => error.ToProblem());
    }

    private static async Task<IResult> GetAppointment(
        Guid id,
        GetAppointmentHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAppointmentQuery(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> RescheduleAppointment(
        Guid id,
        RescheduleAppointmentRequest request,
        RescheduleAppointmentHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RescheduleAppointmentCommand(id, request.BayId!.Value, request.Start!.Value, request.End!.Value),
            cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }
}
