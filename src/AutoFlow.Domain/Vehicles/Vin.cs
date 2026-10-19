using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Vehicles;

/// <summary>
/// Vehicle Identification Number (ISO 3779): 17 characters, digits and capital
/// letters, never I, O or Q. The North-American check digit (position 9) is not
/// enforced because European VINs are not required to carry one.
/// </summary>
public sealed record Vin
{
    public const int Length = 17;

    private Vin(string value) => Value = value;

    public string Value { get; }

    /// <summary>World Manufacturer Identifier (first three characters).</summary>
    public string Wmi => Value[..3];

    public static Vin Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("VIN is required.");
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != Length)
        {
            throw new DomainException($"VIN must be exactly {Length} characters.");
        }

        foreach (var c in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                throw new DomainException($"VIN contains an invalid character '{c}'.");
            }

            if (c is 'I' or 'O' or 'Q')
            {
                throw new DomainException("VIN must not contain the letters I, O or Q.");
            }
        }

        return new Vin(normalized);
    }

    public override string ToString() => Value;
}
