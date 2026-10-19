using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardRouteTests : ApiTests
{
    public BoardRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_workspace_lists_only_its_own_boards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var other = await CreateWorkspaceAsync("Ops");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        await CreateBoardAsync(other.Id, "On call");

        var boards = await BoardsAsync(workspace.Id);

        Assert.Equal(board.Id, Assert.Single(boards).Id);
    }

    [Fact]
    public async Task The_collection_filters_by_searchTerm_archived_and_visibility()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var sprint = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var roadmap = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Public);
        var archived = await CreateBoardAsync(workspace.Id, "Sprint 13");
        await SetBoardArchivedAsync(archived.Id, true);

        Assert.Equal(sprint.Id, Assert.Single(await BoardsAsync(workspace.Id, "searchTerm=sprint")).Id);
        Assert.Equal(archived.Id, Assert.Single(await BoardsAsync(workspace.Id, "archived=only")).Id);
        Assert.Equal(roadmap.Id, Assert.Single(await BoardsAsync(workspace.Id, "visibility=public")).Id);
    }

    [Fact]
    public async Task A_create_puts_the_board_in_the_path_workspace()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await PostAsync(BoardsPath(workspace.Id), new
        {
            name = "Sprint 14",
            description = "Two weeks",
            visibility = "Private"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var board = (await response.ReadEnvelope<BoardDto>()).Data!;
        Assert.Equal(workspace.Id, board.WorkspaceId);
        Assert.Equal("Two weeks", board.Description);
        Assert.Equal(BoardVisibility.Private, board.Visibility);
        Assert.Equal($"/api/v1/boards/{board.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal(board.Id, Assert.Single(await BoardsAsync(workspace.Id)).Id);
    }

    [Fact]
    public async Task An_unknown_workspace_is_404_on_the_read_and_the_create()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync(BoardsPath(missing));
        using var create = await PostAsync(BoardsPath(missing), new { name = "Sprint 14", visibility = "Workspace" });

        foreach (var response in new[] { read, create })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Workspace not found", (await response.ReadEnvelope<object>()).Message);
        }
    }

    [Fact]
    public async Task WorkspaceId_in_the_query_is_unknownField()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await GetAsync($"{BoardsPath(workspace.Id)}?workspaceId={workspace.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("workspaceId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task WorkspaceId_in_the_create_body_is_unknownField()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await PostAsync(BoardsPath(workspace.Id), new
        {
            name = "Sprint 14",
            workspaceId = workspace.Id,
            visibility = "Workspace"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("workspaceId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Body_and_query_errors_come_back_before_the_workspace_is_looked_up()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync($"{BoardsPath(missing)}?visibility=Nope&color=red");
        using var create = await PostAsync(BoardsPath(missing), new { description = "Two weeks" });

        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.Equal(["color", "visibility"], (await read.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal("name", Assert.Single(await create.ReadErrors()).Field);
    }

    [Fact]
    public async Task The_flat_board_collection_routes_are_gone()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var read = await GetAsync($"/api/v1/boards?workspaceId={workspace.Id}");
        using var create = await PostAsync("/api/v1/boards", new
        {
            name = "Sprint 14",
            workspaceId = workspace.Id,
            visibility = "Workspace"
        });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Empty(await BoardsAsync(workspace.Id));
    }

    private async Task<List<BoardDto>> BoardsAsync(Guid workspaceId, string? query = null)
    {
        var path = query is null ? BoardsPath(workspaceId) : $"{BoardsPath(workspaceId)}?{query}";
        using var response = await GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<BoardDto>()).Entries;
    }
}
