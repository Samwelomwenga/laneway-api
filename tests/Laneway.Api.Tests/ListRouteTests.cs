using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ListRouteTests : ApiTests
{
    public ListRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_board_lists_only_its_own_lists()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var other = await CreateBoardAsync(workspace.Id, "Sprint 15");
        var list = await CreateListAsync(board.Id, "To do");
        await CreateListAsync(other.Id, "Backlog");

        var lists = await ListsAsync(board.Id);

        Assert.Equal(list.Id, Assert.Single(lists).Id);
    }

    [Fact]
    public async Task The_collection_filters_by_searchTerm_and_archived()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var todo = await CreateListAsync(board.Id, "To do");
        var doing = await CreateListAsync(board.Id, "Doing");
        var archived = await CreateListAsync(board.Id, "Done");
        await SetListArchivedAsync(archived.Id, true);

        Assert.Equal(todo.Id, Assert.Single(await ListsAsync(board.Id, "searchTerm=to")).Id);
        Assert.Equal([todo.Id, doing.Id], (await ListsAsync(board.Id)).Select(l => l.Id));
        Assert.Equal(archived.Id, Assert.Single(await ListsAsync(board.Id, "archived=only")).Id);
        Assert.Equal([todo.Id, doing.Id, archived.Id], (await ListsAsync(board.Id, "archived=include")).Select(l => l.Id));
    }

    [Fact]
    public async Task A_create_puts_the_list_on_the_path_board()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var done = await CreateListAsync(board.Id, "Done");

        using var response = await PostAsync(ListsPath(board.Id), new
        {
            name = "Doing",
            color = "Blue",
            before = done.Id
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var list = (await response.ReadEnvelope<ListDto>()).Data!;
        Assert.Equal(board.Id, list.BoardId);
        Assert.Equal(Color.Blue, list.Color);
        Assert.Equal($"/api/v1/lists/{list.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal([list.Id, done.Id], (await ListsAsync(board.Id)).Select(l => l.Id));
    }

    [Fact]
    public async Task An_anchor_on_another_board_is_notSibling()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var other = await CreateBoardAsync(workspace.Id, "Sprint 15");
        var elsewhere = await CreateListAsync(other.Id, "Backlog");

        using var response = await PostAsync(ListsPath(board.Id), new { name = "Doing", after = elsewhere.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotSibling, error.Code);
    }

    [Fact]
    public async Task An_unknown_board_is_404_on_the_read_and_the_create()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync(ListsPath(missing));
        using var create = await PostAsync(ListsPath(missing), new { name = "To do" });

        foreach (var response in new[] { read, create })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Board not found", (await response.ReadEnvelope<object>()).Message);
        }
    }

    [Fact]
    public async Task BoardId_in_the_query_is_unknownField()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await GetAsync($"{ListsPath(board.Id)}?boardId={board.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task BoardId_in_the_create_body_is_unknownField()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await PostAsync(ListsPath(board.Id), new { name = "To do", boardId = board.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Body_and_query_errors_come_back_before_the_board_is_looked_up()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync($"{ListsPath(missing)}?archived=Nope&color=red");
        using var create = await PostAsync(ListsPath(missing), new { position = "middle" });

        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.Equal(["archived", "color"], (await read.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(["name", "position"], (await create.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_archived_board_still_lists_its_lists()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var todo = await CreateListAsync(board.Id, "To do");
        var done = await CreateListAsync(board.Id, "Done");
        await SetListArchivedAsync(done.Id, true);
        await SetBoardArchivedAsync(board.Id, true);

        Assert.Equal(todo.Id, Assert.Single(await ListsAsync(board.Id)).Id);
        Assert.Equal(done.Id, Assert.Single(await ListsAsync(board.Id, "archived=only")).Id);
    }

    [Fact]
    public async Task A_create_on_an_archived_board_is_409_archived_with_no_field()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        await SetBoardArchivedAsync(board.Id, true);

        using var response = await PostAsync(ListsPath(board.Id), new { name = "To do" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
        Assert.Empty(await ListsAsync(board.Id, "archived=include"));
    }

    [Fact]
    public async Task The_flat_list_collection_routes_are_gone()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var read = await GetAsync($"/api/v1/lists?boardId={board.Id}");
        using var create = await PostAsync("/api/v1/lists", new { name = "To do", boardId = board.Id });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Empty(await ListsAsync(board.Id));
    }

    private async Task<List<ListDto>> ListsAsync(Guid boardId, string? query = null)
    {
        var path = query is null ? ListsPath(boardId) : $"{ListsPath(boardId)}?{query}";
        using var response = await GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<ListDto>()).Entries;
    }
}
