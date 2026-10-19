namespace AutoFlow.Domain.Common;

/// <summary>
/// Raised when an operation would violate a business rule or invariant.
/// The API layer translates this into a 400/422 problem response.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainException()
    {
    }
}
