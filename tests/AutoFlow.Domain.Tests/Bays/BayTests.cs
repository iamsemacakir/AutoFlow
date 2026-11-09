using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Tests.Bays;

public class BayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_makes_an_in_service_bay_with_a_trimmed_name()
    {
        var bay = Bay.Create("  Lift 1 ", Now);

        Assert.NotEqual(Guid.Empty, bay.Id);
        Assert.Equal("Lift 1", bay.Name);
        Assert.True(bay.IsInService);
        Assert.Equal(Now, bay.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_name(string? name)
    {
        Assert.Throws<DomainException>(() => Bay.Create(name!, Now));
    }

    [Fact]
    public void Create_rejects_an_overlong_name()
    {
        Assert.Throws<DomainException>(() => Bay.Create(new string('x', Bay.NameMaxLength + 1), Now));
    }

    [Fact]
    public void Rename_replaces_the_name()
    {
        var bay = Bay.Create("Lift 1", Now);

        bay.Rename(" Alignment ");

        Assert.Equal("Alignment", bay.Name);
    }

    [Fact]
    public void Rename_rejects_a_blank_name_and_keeps_the_old_one()
    {
        var bay = Bay.Create("Lift 1", Now);

        Assert.Throws<DomainException>(() => bay.Rename(" "));
        Assert.Equal("Lift 1", bay.Name);
    }

    [Fact]
    public void TakeOutOfService_and_ReturnToService_toggle_availability()
    {
        var bay = Bay.Create("Lift 1", Now);

        bay.TakeOutOfService();
        Assert.False(bay.IsInService);

        bay.ReturnToService();
        Assert.True(bay.IsInService);
    }

    [Fact]
    public void TakeOutOfService_twice_is_rejected()
    {
        var bay = Bay.Create("Lift 1", Now);
        bay.TakeOutOfService();

        Assert.Throws<DomainException>(() => bay.TakeOutOfService());
        Assert.False(bay.IsInService);
    }

    [Fact]
    public void ReturnToService_of_an_in_service_bay_is_rejected()
    {
        var bay = Bay.Create("Lift 1", Now);

        Assert.Throws<DomainException>(() => bay.ReturnToService());
        Assert.True(bay.IsInService);
    }
}
