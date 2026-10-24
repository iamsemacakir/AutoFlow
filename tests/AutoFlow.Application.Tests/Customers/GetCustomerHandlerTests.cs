using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Tests.Fakes;

namespace AutoFlow.Application.Tests.Customers;

public class GetCustomerHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly GetCustomerHandler _handler;

    public GetCustomerHandlerTests() => _handler = new GetCustomerHandler(_store);

    [Fact]
    public async Task Handle_returns_the_customer_details()
    {
        var customer = _store.Seed(TestData.Customer());

        var result = await _handler.HandleAsync(new GetCustomerQuery(customer.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(CustomerDetails.From(customer), result.Value);
    }

    [Fact]
    public async Task Handle_unknown_customer_returns_not_found()
    {
        var result = await _handler.HandleAsync(new GetCustomerQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("customer.not_found", result.Error.Code);
    }
}
