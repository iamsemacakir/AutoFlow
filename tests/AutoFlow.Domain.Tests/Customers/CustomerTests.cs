using AutoFlow.Domain.Common;
using AutoFlow.Domain.Customers;

namespace AutoFlow.Domain.Tests.Customers;

public class CustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_with_valid_data_normalizes_fields()
    {
        var customer = Customer.Create("  Anna ", " Schmidt", "Anna.Schmidt@Example.COM ", " +49 30 1234567 ", Now);

        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Equal("Anna", customer.FirstName);
        Assert.Equal("Schmidt", customer.LastName);
        Assert.Equal("Anna Schmidt", customer.FullName);
        Assert.Equal("anna.schmidt@example.com", customer.Email.Value);
        Assert.Equal("+49 30 1234567", customer.PhoneNumber);
        Assert.Equal(Now, customer.CreatedAt);
    }

    [Theory]
    [InlineData("", "Schmidt")]
    [InlineData("   ", "Schmidt")]
    [InlineData("Anna", "")]
    public void Create_requires_names(string firstName, string lastName)
    {
        Assert.Throws<DomainException>(() =>
            Customer.Create(firstName, lastName, "anna@example.com", null, Now));
    }

    [Fact]
    public void Create_rejects_overlong_name()
    {
        var name = new string('a', Customer.NameMaxLength + 1);

        Assert.Throws<DomainException>(() =>
            Customer.Create(name, "Schmidt", "anna@example.com", null, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Phone_number_is_optional(string? phone)
    {
        var customer = Customer.Create("Anna", "Schmidt", "anna@example.com", phone, Now);

        Assert.Null(customer.PhoneNumber);
    }

    [Theory]
    [InlineData("call me")]
    [InlineData("+49 30 12a4")]
    [InlineData("---")]
    public void Create_rejects_invalid_phone_numbers(string phone)
    {
        Assert.Throws<DomainException>(() =>
            Customer.Create("Anna", "Schmidt", "anna@example.com", phone, Now));
    }

    [Fact]
    public void Rename_updates_names()
    {
        var customer = Customer.Create("Anna", "Schmidt", "anna@example.com", null, Now);

        customer.Rename("Anna", "Müller");

        Assert.Equal("Anna Müller", customer.FullName);
    }

    [Fact]
    public void UpdateContactDetails_replaces_email_and_phone()
    {
        var customer = Customer.Create("Anna", "Schmidt", "anna@example.com", "0301234", Now);

        customer.UpdateContactDetails("anna@work.example", null);

        Assert.Equal("anna@work.example", customer.Email.Value);
        Assert.Null(customer.PhoneNumber);
    }

    [Fact]
    public void Customers_with_same_id_are_equal_and_different_customers_are_not()
    {
        var a = Customer.Create("Anna", "Schmidt", "anna@example.com", null, Now);
        var b = Customer.Create("Anna", "Schmidt", "anna@example.com", null, Now);

        Assert.Equal(a, a);
        Assert.NotEqual(a, b);
    }
}
