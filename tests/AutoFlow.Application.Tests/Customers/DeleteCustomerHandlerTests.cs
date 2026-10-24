using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Tests.Fakes;

namespace AutoFlow.Application.Tests.Customers;

public class DeleteCustomerHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly DeleteCustomerHandler _handler;

    public DeleteCustomerHandlerTests() => _handler = new DeleteCustomerHandler(_store, _store, _store);

    [Fact]
    public async Task Handle_deletes_a_customer_without_vehicles()
    {
        var customer = _store.Seed(TestData.Customer());

        var result = await _handler.HandleAsync(new DeleteCustomerCommand(customer.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(customer.Id, result.Value.Id);
        Assert.Empty(_store.Customers);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_refuses_to_delete_a_customer_who_owns_vehicles()
    {
        var customer = _store.Seed(TestData.Customer());
        _store.Seed(TestData.Vehicle(customer.Id));

        var result = await _handler.HandleAsync(new DeleteCustomerCommand(customer.Id));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal("customer.has_vehicles", result.Error.Code);
        Assert.Single(_store.Customers);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_unknown_customer_returns_not_found()
    {
        var result = await _handler.HandleAsync(new DeleteCustomerCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("customer.not_found", result.Error.Code);
        Assert.Equal(0, _store.SaveCount);
    }
}
