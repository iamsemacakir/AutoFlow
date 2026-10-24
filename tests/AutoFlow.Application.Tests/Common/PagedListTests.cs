using AutoFlow.Application.Common;

namespace AutoFlow.Application.Tests.Common;

public class PagedListTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public void TotalPages_rounds_up(int totalCount, int pageSize, int expected)
    {
        var page = new PagedList<int>([], 1, pageSize, totalCount);

        Assert.Equal(expected, page.TotalPages);
    }

    [Fact]
    public void Map_projects_items_and_keeps_paging_data()
    {
        var page = new PagedList<int>([1, 2], 3, 2, 7);

        var mapped = page.Map(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal("1,2", string.Join(",", mapped.Items));
        Assert.Equal((3, 2, 7, 4), (mapped.Page, mapped.PageSize, mapped.TotalCount, mapped.TotalPages));
    }
}
