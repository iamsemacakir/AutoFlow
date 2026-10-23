namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when an aggregate was
/// changed by someone else between loading and saving it (optimistic
/// concurrency). Nothing of the unit of work was committed; the client should
/// reload and retry. The API answers with 409 Conflict.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("The data was changed by another request after it was loaded.")
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
