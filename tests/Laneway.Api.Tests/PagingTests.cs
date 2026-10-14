using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class PagingTests : ApiTests
{
    public PagingTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_search_without_a_page_size_returns_twenty_items()
    {
        for (var i = 0; i < 21; i++)
        {
            await CreateWorkspaceAsync($"Workspace {i}");
        }

        using var response = await GetAsync("/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadPage<WorkSpaceDto>();
        Assert.Equal(20, page.Entries.Count);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(21, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }
}
