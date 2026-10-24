namespace AutoFlow.Application.Common;

/// <summary>
/// Category of an expected failure. The API layer maps each kind to an HTTP
/// status code (Validation → 400, NotFound → 404, Conflict → 409).
/// </summary>
public enum ErrorKind
{
    NotFound,
    Conflict,

    /// <summary>A request parameter is outside what the use case accepts, e.g. a page size of 0.</summary>
    Validation,
}
