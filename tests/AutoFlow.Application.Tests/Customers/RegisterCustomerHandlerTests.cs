using AutoFlow.Application.Customers;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Domain.Common;

namespace AutoFlow.Application.Tests.Customers;

public class RegisterCustomerHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly RegisterCustomerHandler _handler;

    public RegisterCustomerHandlerTests() =>
        _handler = new RegisterCustomerHandler(_store, _store, new FixedTimeProvider());

    [Fact]
    public async Task Handle_stores_customer_with_time_from_time_provider()
    {
        var result = await _handler.HandleAsync(
            new RegisterCustomerCommand(" Anna ", "Schmidt", "Anna@Example.com", "+49 30 1234567"));

        Assert.True(result.IsSuccess);
        var details = result.Value;
        Assert.Equal("Anna", details.FirstName);
        Assert.Equal("anna@example.com", details.Email);
        Assert.Equal(FixedTimeProvider.Default, details.CreatedAt);

        var stored = Assert.Single(_store.Customers);
        Assert.Equal(details.Id, stored.Id);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_with_invalid_email_throws_and_saves_nothing()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(new RegisterCustomerCommand("Anna", "Schmidt", "not-an-email", null)));

        Assert.Empty(_store.Customers);
        Assert.Equal(0, _store.SaveCount);
    }
}
