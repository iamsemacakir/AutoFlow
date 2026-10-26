namespace AutoFlow.Api.Validation;

/// <summary>
/// Collects field-level problems with a request body. Field names are the
/// JSON property names (camelCase), so a client can show each message next to
/// the input it belongs to.
/// </summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsEmpty => _errors.Count == 0;

    public void Add(string field, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors.Add(field, messages);
        }

        messages.Add(message);
    }

    /// <summary>The value must be present, not blank and at most <paramref name="maxLength"/> characters once trimmed.</summary>
    public void RequireText(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, $"'{field}' is required.");
            return;
        }

        CheckLength(field, value, maxLength);
    }

    /// <summary>The value may be missing or blank; if given it must be at most <paramref name="maxLength"/> characters once trimmed.</summary>
    public void OptionalText(string field, string? value, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            CheckLength(field, value, maxLength);
        }
    }

    /// <summary>The value must be present and at least <paramref name="minimum"/>.</summary>
    public void RequireAtLeast(string field, int? value, int minimum)
    {
        if (value is null)
        {
            Add(field, $"'{field}' is required.");
        }
        else if (value < minimum)
        {
            Add(field, $"'{field}' must be {minimum} or greater.");
        }
    }

    /// <summary>The value must be present and not the empty id.</summary>
    public void RequireId(string field, Guid? value)
    {
        if (value is null || value == Guid.Empty)
        {
            Add(field, $"'{field}' is required.");
        }
    }

    public Dictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);

    private void CheckLength(string field, string value, int maxLength)
    {
        if (value.Trim().Length > maxLength)
        {
            Add(field, $"'{field}' must be at most {maxLength} characters.");
        }
    }
}
