using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AutoFlow.Api.Errors;

/// <summary>
/// Turns a request the framework could not bind (malformed JSON, a field of
/// the wrong type, an unparsable query value, a non-JSON body) into a problem
/// response with the status the framework chose (400 or 415) and a stable
/// code. The host sets <c>RouteHandlerOptions.ThrowOnBadRequest</c> so this
/// path is the same in every environment.
/// </summary>
internal sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        var status = badRequest.StatusCode;
        var (title, detail) = status switch
        {
            StatusCodes.Status415UnsupportedMediaType =>
                ("Unsupported media type", "Send the request body as JSON with Content-Type: application/json."),
            _ => ("Malformed request",
                "The request could not be read. Check that the body is valid JSON with the expected field types " +
                "and that route and query values have the expected format."),
        };

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { [ProblemDetailsConventions.CodeKey] = ProblemCodes.ForStatus(status) },
            },
        });
    }
}
