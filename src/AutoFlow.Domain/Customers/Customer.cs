using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Customers;

/// <summary>
/// A workshop customer: the person who brings vehicles in and pays invoices.
/// </summary>
public sealed class Customer : Entity
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 32;

    private Customer(
        Guid id,
        string firstName,
        string lastName,
        EmailAddress email,
        string? phoneNumber,
        DateTimeOffset createdAt)
        : base(id)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PhoneNumber = phoneNumber;
        CreatedAt = createdAt;
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public EmailAddress Email { get; private set; }

    public string? PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static Customer Create(
        string firstName,
        string lastName,
        string email,
        string? phoneNumber,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            Guard.RequiredText(firstName, "First name", NameMaxLength),
            Guard.RequiredText(lastName, "Last name", NameMaxLength),
            EmailAddress.Create(email),
            NormalizePhone(phoneNumber),
            createdAt);

    public void Rename(string firstName, string lastName)
    {
        FirstName = Guard.RequiredText(firstName, "First name", NameMaxLength);
        LastName = Guard.RequiredText(lastName, "Last name", NameMaxLength);
    }

    public void UpdateContactDetails(string email, string? phoneNumber)
    {
        Email = EmailAddress.Create(email);
        PhoneNumber = NormalizePhone(phoneNumber);
    }

    private static string? NormalizePhone(string? phoneNumber)
    {
        var phone = Guard.OptionalText(phoneNumber, "Phone number", PhoneMaxLength);
        if (phone is null)
        {
            return null;
        }

        foreach (var c in phone)
        {
            if (!(char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')' or '/'))
            {
                throw new DomainException($"Phone number contains an invalid character '{c}'.");
            }
        }

        if (!phone.Any(char.IsAsciiDigit))
        {
            throw new DomainException("Phone number must contain digits.");
        }

        return phone;
    }
}
