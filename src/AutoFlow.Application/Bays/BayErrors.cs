using AutoFlow.Application.Common;

namespace AutoFlow.Application.Bays;

public static class BayErrors
{
    public static Error NotFound(Guid bayId) =>
        Error.NotFound("bay.not_found", $"Bay '{bayId}' was not found.");

    public static Error DuplicateName(string name) =>
        Error.Conflict("bay.duplicate_name", $"A bay named '{name}' already exists.");
}
