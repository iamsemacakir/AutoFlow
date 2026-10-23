using AutoFlow.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AutoFlow.Api.Errors;

/// <summary>
/// Turns the conflicts the unit of work reports after a lost race into an
/// RFC 9457 problem response with status 409 instead of a 500. The response
/// carries a stable <c>code</c> for clients but no database details such as
/// constraint names; those only go to the log.
/// </summary>
internal sealed partial class ConflictExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ConflictExceptionHandler> logger) : IExceptionHandler
{
    public const string ConcurrencyConflictCode = "conflict.concurrent_update";
    public const string DuplicateCode = "conflict.duplicate";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        ProblemDetails? problem = exception switch
        {
            ConcurrencyConflictException => new ProblemDetails
            {
                Title = "Concurrent update",
                Detail = "The resource was changed by another request after it was loaded. Reload it and try again.",
                Extensions = { ["code"] = ConcurrencyConflictCode },
            },
            UniqueConstraintViolationException => new ProblemDetails
            {
                Title = "Duplicate value",
                Detail = "Another request has just stored a record with the same unique value.",
                Extensions = { ["code"] = DuplicateCode },
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        LogConflict(logger, exception.GetType().Name, (exception as UniqueConstraintViolationException)?.ConstraintName);

        problem.Status = StatusCodes.Status409Conflict;
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request lost a write race: {ConflictType} (constraint: {ConstraintName})")]
    private static partial void LogConflict(ILogger logger, string conflictType, string? constraintName);
}
