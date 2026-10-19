using AutoFlow.Domain.Common;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Domain.Tests.Vehicles;

public class VinTests
{
    [Theory]
    [InlineData("WVWZZZ1JZXW000001", "WVWZZZ1JZXW000001")]
    [InlineData(" wba3a5c51cf256651 ", "WBA3A5C51CF256651")]
    public void Create_accepts_and_normalizes_valid_vins(string input, string expected)
    {
        var vin = Vin.Create(input);

        Assert.Equal(expected, vin.Value);
        Assert.Equal(expected[..3], vin.Wmi);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WVWZZZ1JZXW00000")]   // 16 chars
    [InlineData("WVWZZZ1JZXW0000012")] // 18 chars
    [InlineData("WVWZZZ1JZXW00000I")]  // contains I
    [InlineData("WVWZZZ1JZXW00000O")]  // contains O
    [InlineData("WVWZZZ1JZXW00000Q")]  // contains Q
    [InlineData("WVWZZZ1JZXW-00001")]  // punctuation
    public void Create_rejects_invalid_vins(string? input)
    {
        Assert.Throws<DomainException>(() => Vin.Create(input));
    }
}
