namespace AutoFlow.Application.Common;

/// <summary>Limits shared by every paged query.</summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Returns a validation error when <paramref name="page"/> or
    /// <paramref name="pageSize"/> is out of range, otherwise <c>null</c>.
    /// Pages so far out that the offset would overflow are rejected too.
    /// </summary>
    public static Error? Validate(int page, int pageSize)
    {
        if (pageSize is < 1 or > MaxPageSize)
        {
            return Error.Validation(
                "paging.invalid_page_size",
                $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (page < 1 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            return Error.Validation("paging.invalid_page", "Page must be a positive number within range.");
        }

        return null;
    }

    /// <summary>Number of items before <paramref name="page"/>; only call after <see cref="Validate"/>.</summary>
    public static int Offset(int page, int pageSize) => (page - 1) * pageSize;
}
