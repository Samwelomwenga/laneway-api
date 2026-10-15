using System.Net;
using System.Text.Json;
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
        await CreateWorkspacesAsync(21);

        using var response = await GetAsync("/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadPage<WorkSpaceDto>();
        Assert.Equal(20, page.Entries.Count);
        Assert.Equal(new Pagination(1, 20, 21, 2, HasNextPage: true, HasPreviousPage: false), page.Pagination);
    }

    [Fact]
    public async Task The_last_page_has_a_previous_page_and_no_next_page()
    {
        await CreateWorkspacesAsync(5);

        using var response = await GetAsync("/api/v1/workspaces?pageNumber=3&pageSize=2");

        var page = await response.ReadPage<WorkSpaceDto>();
        Assert.Single(page.Entries);
        Assert.Equal(new Pagination(3, 2, 5, 3, HasNextPage: false, HasPreviousPage: true), page.Pagination);
    }

    [Fact]
    public async Task An_empty_search_has_no_pages()
    {
        using var response = await GetAsync("/api/v1/workspaces");

        var page = await response.ReadPage<WorkSpaceDto>();
        Assert.Equal(new Pagination(1, 20, 0, 0, HasNextPage: false, HasPreviousPage: false), page.Pagination);
    }

    [Fact]
    public async Task Paging_fields_sit_only_in_the_pagination_object()
    {
        await CreateWorkspacesAsync(1);

        using var response = await GetAsync("/api/v1/workspaces");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = body.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        Assert.Contains("pagination", keys);
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("data").ValueKind);
        Assert.DoesNotContain("totalCount", keys);
        Assert.DoesNotContain("pageSize", keys);
        Assert.DoesNotContain("currentPage", keys);
        Assert.DoesNotContain("totalPages", keys);
        Assert.DoesNotContain("hasNextPage", keys);
        Assert.DoesNotContain("hasPreviousPage", keys);
    }

    [Fact]
    public async Task A_rejected_search_has_no_pagination_object()
    {
        using var response = await GetAsync("/api/v1/workspaces?pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("pagination", out _));
    }

    private async Task CreateWorkspacesAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await CreateWorkspaceAsync($"Workspace {i}");
        }
    }
}
