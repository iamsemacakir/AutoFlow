namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when the database
/// rejects a write because a unique value (such as a VIN) is already taken.
/// Use cases check uniqueness up front and return a specific error; this
/// exception covers the race where two requests pass that check at the same
/// time. Nothing was committed. The API answers with 409 Conflict.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException()
        : base("A record with the same unique value already exists.")
    {
    }

    public UniqueConstraintViolationException(string message)
        : base(message)
    {
    }

    public UniqueConstraintViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UniqueConstraintViolationException(string message, string? constraintName, Exception innerException)
        : base(message, innerException)
    {
        ConstraintName = constraintName;
    }

    /// <summary>
    /// Name of the violated unique index (e.g. <c>ux_vehicles_vin</c>), for
    /// logs and diagnostics. Never sent to clients.
    /// </summary>
    public string? ConstraintName { get; }
}
