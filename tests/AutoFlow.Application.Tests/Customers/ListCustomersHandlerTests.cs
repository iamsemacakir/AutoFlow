using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Tests.Fakes;

namespace AutoFlow.Application.Tests.Customers;

public class ListCustomersHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly ListCustomersHandler _handler;

    public ListCustomersHandlerTests() => _handler = new ListCustomersHandler(_store);

    [Fact]
    public async Task Handle_without_search_pages_through_all_customers_by_name()
    {
        _store.Seed(TestData.Customer("Mia", "Wagner"));
        _store.Seed(TestData.Customer("Jonas", "Becker"));
        _store.Seed(TestData.Customer("Anna", "Schmidt"));

        var first = await _handler.HandleAsync(new ListCustomersQuery(Page: 1, PageSize: 2));
        var second = await _handler.HandleAsync(new ListCustomersQuery(Page: 2, PageSize: 2));

        Assert.Equal(["Becker", "Schmidt"], first.Value.Items.Select(c => c.LastName).ToArray());
        Assert.Equal("Wagner", Assert.Single(second.Value.Items).LastName);
        Assert.Equal(3, first.Value.TotalCount);
        Assert.Equal(2, first.Value.TotalPages);
        Assert.Equal(2, second.Value.Page);
    }

    [Fact]
    public async Task Handle_requires_every_search_term_to_match()
    {
        _store.Seed(TestData.Customer("Anna", "Schmidt"));
        _store.Seed(TestData.Customer("Anna", "Becker"));
        _store.Seed(TestData.Customer("Jonas", "Schmidt"));

        var result = await _handler.HandleAsync(new ListCustomersQuery("  anna   SCHMIDT "));

        var match = Assert.Single(result.Value.Items);
        Assert.Equal(("Anna", "Schmidt"), (match.FirstName, match.LastName));
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task Handle_page_past_the_end_is_empty_but_keeps_the_total()
    {
        _store.Seed(TestData.Customer());

        var result = await _handler.HandleAsync(new ListCustomersQuery(Page: 5));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Theory]
    [InlineData(0, 20, "paging.invalid_page")]
    [InlineData(1, 0, "paging.invalid_page_size")]
    [InlineData(1, Paging.MaxPageSize + 1, "paging.invalid_page_size")]
    [InlineData(int.MaxValue, Paging.MaxPageSize, "paging.invalid_page")]
    public async Task Handle_rejects_paging_out_of_range(int page, int pageSize, string code)
    {
        var result = await _handler.HandleAsync(new ListCustomersQuery(Page: page, PageSize: pageSize));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_rejects_an_overlong_search_text()
    {
        var result = await _handler.HandleAsync(
            new ListCustomersQuery(new string('a', ListCustomersHandler.SearchMaxLength + 1)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal("customer.search_too_long", result.Error.Code);
    }
}
