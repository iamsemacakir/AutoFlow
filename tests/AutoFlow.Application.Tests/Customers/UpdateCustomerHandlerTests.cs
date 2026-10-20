using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Tests.Fakes;

namespace AutoFlow.Application.Tests.Customers;

public class UpdateCustomerHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly UpdateCustomerHandler _handler;

    public UpdateCustomerHandlerTests() => _handler = new UpdateCustomerHandler(_store, _store);

    [Fact]
    public async Task Handle_updates_name_and_contact_details()
    {
        var customer = _store.Seed(TestData.Customer());

        var result = await _handler.HandleAsync(new UpdateCustomerCommand(
            customer.Id, "Anna", "Müller", "anna.mueller@example.com", "0171 2345678"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Müller", customer.LastName);
        Assert.Equal("anna.mueller@example.com", customer.Email.Value);
        Assert.Equal("0171 2345678", result.Value.PhoneNumber);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_unknown_customer_returns_not_found()
    {
        var id = Guid.NewGuid();

        var result = await _handler.HandleAsync(new UpdateCustomerCommand(id, "Anna", "Schmidt", "a@example.com", null));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("customer.not_found", result.Error.Code);
        Assert.Contains(id.ToString(), result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _store.SaveCount);
    }
}
