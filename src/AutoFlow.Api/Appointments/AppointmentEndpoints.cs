using AutoFlow.Api.Auth;
using AutoFlow.Api.Concurrency;
using AutoFlow.Api.Errors;
using AutoFlow.Api.Observability;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.WorkOrders;

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
/// Optional request body for checking a vehicle in: the odometer reading at
/// arrival (also recorded on the vehicle) and what the customer reports at
/// the counter (else the appointment's purpose becomes the complaint).
/// </summary>
public sealed record CheckInAppointmentRequest(int? MileageKm, string? Complaint) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // A reading below the vehicle's last one is a business rule (422).
        errors.OptionalAtLeast("mileageKm", MileageKm, 0);
        errors.OptionalText("complaint", Complaint, WorkOrder.ComplaintMaxLength);
    }
}

/// <summary>Request body for calling off a scheduled appointment.</summary>
public sealed record CancelAppointmentRequest(string? Reason) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireText("reason", Reason, Appointment.CancellationReasonMaxLength);
    }
}

/// <summary>
/// Appointments are booked under the vehicle that is brought in
/// (<c>POST /api/vehicles/{vehicleId}/appointments</c>, where <c>GET</c>
/// lists the vehicle's appointments, latest first) and addressed on
/// their own afterwards (<c>/api/appointments/{id}</c>), like work orders,
/// so the URL survives a sale of the car. Moving one is an action
/// (<c>/reschedule</c>) because it is checked against the other
/// appointments, not a plain replacement. A bay out of service or a clash
/// with another appointment answers 409 with a specific code; a slot off the
/// 15-minute grid or in the past answers 422. A scheduled appointment is
/// settled by one of three actions named after the domain methods:
/// <c>/check-in</c> (answers with the appointment and the draft work order
/// opened for it), <c>/cancel</c> (takes a <c>reason</c>) and
/// <c>/no-show</c>; settling it twice answers 422.
/// <c>GET /api/schedule?date=…&amp;timeZone=…</c> is the front desk's view of
/// one day: every bay with its appointments (ADR 0017).
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
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Books a bay and slot for the vehicle, for its current owner.")
            .Produces<AppointmentDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        byVehicle.MapGet("/", ListVehicleAppointments)
            .WithName("ListVehicleAppointments")
            .RequireAuthorization(Policies.Staff)
            .ProducesAuthProblems()
            .WithSummary("Lists a vehicle's appointments page by page, latest start first, optionally by status.")
            .Produces<PagedList<AppointmentDetails>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var appointments = app.MapGroup("/api/appointments").WithTags(Tag).WithRequestValidation();

        appointments.MapGet("/{id:guid}", GetAppointment)
            .WithRowVersion<Appointment>(readOnly: true)
            .WithName(GetAppointmentRoute)
            .RequireAuthorization(Policies.Staff)
            .ProducesAuthProblems()
            .WithSummary("Gets an appointment, in any status.")
            .Produces<AppointmentDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        appointments.MapPost("/{id:guid}/reschedule", RescheduleAppointment)
            .WithRowVersion<Appointment>()
            .WithName("RescheduleAppointment")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Moves a scheduled appointment to another slot, another bay, or both.")
            .Produces<AppointmentDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        appointments.MapPost("/{id:guid}/check-in", CheckInAppointment)
            .WithRowVersion<Appointment>()
            .WithName("CheckInAppointment")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("The vehicle has arrived: opens a draft work order for it and checks the appointment in.")
            .Produces<CheckedInAppointment>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        appointments.MapPost("/{id:guid}/cancel", CancelAppointment)
            .WithRowVersion<Appointment>()
            .WithName("CancelAppointment")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Calls off a scheduled appointment, with a reason; its slot is free again at once.")
            .Produces<AppointmentDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        appointments.MapPost("/{id:guid}/no-show", MarkAppointmentNoShow)
            .WithRowVersion<Appointment>()
            .WithName("MarkAppointmentNoShow")
            .RequireAuthorization(Policies.FrontDesk)
            .ProducesAuthProblems()
            .WithSummary("Records that the vehicle did not arrive, once the slot has begun.")
            .Produces<AppointmentDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGroup("/api/schedule").WithTags(Tag)
            .MapGet("/", GetDaySchedule)
            .WithName("GetDaySchedule")
            .RequireAuthorization(Policies.Staff)
            .ProducesAuthProblems()
            .WithSummary("One day's appointments per bay; the day is read in the given IANA time zone (default UTC).")
            .Produces<DaySchedule>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetDaySchedule(
        DateOnly date,
        string? timeZone,
        GetDayScheduleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new GetDayScheduleQuery(date, timeZone ?? GetDayScheduleQuery.Utc),
            cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> ListVehicleAppointments(
        Guid vehicleId,
        string? status,
        int? page,
        int? pageSize,
        ListVehicleAppointmentsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ListVehicleAppointmentsQuery(vehicleId, status, page ?? 1, pageSize ?? Paging.DefaultPageSize),
            cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> BookAppointment(
        Guid vehicleId,
        BookAppointmentRequest request,
        BookAppointmentHandler handler,
        AutoFlowMetrics metrics,
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
            appointment =>
            {
                metrics.AppointmentBooked();
                return Results.CreatedAtRoute(GetAppointmentRoute, new { id = appointment.Id }, appointment);
            },
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

    private static async Task<IResult> CheckInAppointment(
        Guid id,
        CheckInAppointmentRequest? request,
        CheckInAppointmentHandler handler,
        AutoFlowMetrics metrics,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CheckInAppointmentCommand(id, request?.MileageKm, request?.Complaint),
            cancellationToken);

        return result.Match(
            value =>
            {
                metrics.WorkOrderOpened("check-in");
                return Results.Ok(value);
            },
            error => error.ToProblem());
    }

    private static async Task<IResult> CancelAppointment(
        Guid id,
        CancelAppointmentRequest request,
        CancelAppointmentHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelAppointmentCommand(id, request.Reason!), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> MarkAppointmentNoShow(
        Guid id,
        MarkAppointmentNoShowHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new MarkAppointmentNoShowCommand(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }
}
