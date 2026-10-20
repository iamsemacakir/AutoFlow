namespace AutoFlow.Application.Common;

/// <summary>
/// Category of an expected failure. The API layer maps each kind to an HTTP
/// status code (NotFound → 404, Conflict → 409).
/// </summary>
public enum ErrorKind
{
    NotFound,
    Conflict,
}
