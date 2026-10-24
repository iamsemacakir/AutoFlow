using System.Diagnostics.CodeAnalysis;

namespace AutoFlow.Application.Common;

/// <summary>
/// An expected, recoverable failure of a use case, such as a missing customer
/// or a VIN that is already registered. <see cref="Code"/> is stable and meant
/// for clients; <see cref="Message"/> is human readable.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "C#-only code base; 'Error' is the established name for this concept.")]
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
}
