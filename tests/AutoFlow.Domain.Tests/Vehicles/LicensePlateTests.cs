using AutoFlow.Domain.Common;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Domain.Tests.Vehicles;

public class LicensePlateTests
{
    [Theory]
    [InlineData("M-AB 123", "M-AB 123")]
    [InlineData("  m-ab   123 ", "M-AB 123")]
    [InlineData("34 ABC 123", "34 ABC 123")]
    [InlineData("B-ÖÄ 42", "B-ÖÄ 42")]
    public void Create_normalizes_plates(string input, string expected)
    {
        Assert.Equal(expected, LicensePlate.Create(input).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("M_AB_123")]
    [InlineData("- -")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    public void Create_rejects_invalid_plates(string? input)
    {
        Assert.Throws<DomainException>(() => LicensePlate.Create(input));
    }
}
