using System.Net.Mail;
using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Customers;

/// <summary>
/// A normalized (trimmed, lower-cased) e-mail address.
/// </summary>
public sealed record EmailAddress
{
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress Create(string? value)
    {
        var trimmed = Guard.RequiredText(value, "E-mail address", MaxLength).ToLowerInvariant();

        if (!MailAddress.TryCreate(trimmed, out var parsed)
            || parsed.Address != trimmed
            || !parsed.Host.Contains('.', StringComparison.Ordinal))
        {
            throw new DomainException($"'{trimmed}' is not a valid e-mail address.");
        }

        return new EmailAddress(trimmed);
    }

    public override string ToString() => Value;
}
