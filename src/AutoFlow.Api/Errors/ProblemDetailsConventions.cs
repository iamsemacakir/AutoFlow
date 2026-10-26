using System.Diagnostics;

namespace AutoFlow.Api.Errors;

/// <summary>
/// Gives every problem response the same shape, whoever wrote it (a use-case
/// error, an exception handler, request validation or the status-code-pages
/// middleware for unmatched routes): <c>status</c>, <c>title</c>,
/// <c>instance</c> (the request path), a stable <c>code</c> and a
/// <c>traceId</c> to find the request in the logs.
/// </summary>
internal static class ProblemDetailsConventions
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";

    public static void Apply(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = context.ProblemDetails;
        var http = context.HttpContext;

        problem.Status ??= http.Response.StatusCode;
        problem.Instance ??= http.Request.PathBase.Add(http.Request.Path).Value;

        if (!problem.Extensions.ContainsKey(CodeKey))
        {
            problem.Extensions[CodeKey] = ProblemCodes.ForStatus(problem.Status.Value);
        }

        if (!problem.Extensions.ContainsKey(TraceIdKey))
        {
            problem.Extensions[TraceIdKey] = Activity.Current?.Id ?? http.TraceIdentifier;
        }
    }
}
