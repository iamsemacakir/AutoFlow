namespace AutoFlow.Api.Errors;

/// <summary>
/// Stable <c>code</c> values for problem responses that do not come from a
/// use-case <see cref="AutoFlow.Application.Common.Error"/>. Every problem the API
/// writes carries a code; clients switch on it instead of parsing titles.
/// </summary>
public static class ProblemCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string MalformedRequest = "request.malformed";
    public const string UnsupportedMediaType = "request.unsupported_media_type";
    public const string MethodNotAllowed = "request.method_not_allowed";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string RouteNotFound = "route.not_found";
    public const string BusinessRuleViolated = "domain.rule_violated";
    public const string ServerError = "server.error";
    public const string RequestFailed = "request.failed";
    public const string PreconditionFailed = "request.precondition_failed";

    /// <summary>The code used when a problem is written without one, chosen by status.</summary>
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => MalformedRequest,
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => RouteNotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status412PreconditionFailed => PreconditionFailed,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status422UnprocessableEntity => BusinessRuleViolated,
        >= 500 => ServerError,
        _ => RequestFailed,
    };
}
