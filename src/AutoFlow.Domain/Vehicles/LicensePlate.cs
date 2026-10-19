using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Vehicles;

/// <summary>
/// A registration plate, normalized to upper case with single spaces so that
/// "m-ab  123" and "M-AB 123" compare equal.
/// </summary>
public sealed record LicensePlate
{
    public const int MaxLength = 15;

    private LicensePlate(string value) => Value = value;

    public string Value { get; }

    public static LicensePlate Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("License plate is required.");
        }

        var normalized = string.Join(
            ' ',
            value.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length > MaxLength)
        {
            throw new DomainException($"License plate must be at most {MaxLength} characters.");
        }

        foreach (var c in normalized)
        {
            if (!(char.IsLetterOrDigit(c) || c is ' ' or '-'))
            {
                throw new DomainException($"License plate contains an invalid character '{c}'.");
            }
        }

        if (!normalized.Any(char.IsLetterOrDigit))
        {
            throw new DomainException("License plate must contain letters or digits.");
        }

        return new LicensePlate(normalized);
    }

    public override string ToString() => Value;
}
