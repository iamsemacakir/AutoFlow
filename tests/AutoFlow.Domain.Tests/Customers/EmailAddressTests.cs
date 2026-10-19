using AutoFlow.Domain.Common;
using AutoFlow.Domain.Customers;

namespace AutoFlow.Domain.Tests.Customers;

public class EmailAddressTests
{
    [Theory]
    [InlineData("john@example.com", "john@example.com")]
    [InlineData("  John.Doe@Example.COM  ", "john.doe@example.com")]
    [InlineData("a+tag@sub.example.de", "a+tag@sub.example.de")]
    public void Create_accepts_and_normalizes_valid_addresses(string input, string expected)
    {
        Assert.Equal(expected, EmailAddress.Create(input).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("john@localhost")]
    [InlineData("John <john@example.com>")]
    [InlineData("john@@example.com")]
    public void Create_rejects_invalid_addresses(string? input)
    {
        Assert.Throws<DomainException>(() => EmailAddress.Create(input));
    }

    [Fact]
    public void Addresses_compare_by_normalized_value()
    {
        Assert.Equal(EmailAddress.Create("A@Example.com"), EmailAddress.Create("a@example.com"));
    }
}
