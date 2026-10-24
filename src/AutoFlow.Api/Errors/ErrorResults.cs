using AutoFlow.Application.Common;

namespace AutoFlow.Api.Errors;

/// <summary>
/// Turns an expected use-case failure into an RFC 9457 problem response. The
/// status follows <see cref="ErrorKind"/>; the stable <see cref="Error.Code"/>
/// goes into the <c>code</c> extension, like the codes of the exception handlers.
/// </summary>
internal static class ErrorResults
{
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var (status, title) = error.Kind switch
        {
            ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Invalid request"),
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
        };

        return Results.Problem(
            detail: error.Message,
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
