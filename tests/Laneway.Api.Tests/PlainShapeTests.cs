using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class PlainShapeTests : ApiTests
{
    public PlainShapeTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Workspace_reads_and_writes_carry_no_board_ids()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        await CreateBoardAsync(workspace.Id, "Sprint 14");

        await AssertLacksAsync(() => PostAsync("/api/v1/workspaces", new { name = "Ops", visibility = "Public" }), "boardIds");
        await AssertLacksAsync(() => GetAsync($"/api/v1/workspaces/{workspace.Id}"), "boardIds");
        await AssertLacksAsync(() => UpdateWorkspaceAsync(workspace.Id, "Design team"), "boardIds");
        await AssertLacksAsync(() => GetAsync("/api/v1/workspaces"), "boardIds");
    }

    [Fact]
    public async Task Board_reads_and_writes_carry_no_list_ids()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var other = await CreateWorkspaceAsync("Ops");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        await CreateListAsync(board.Id, "To do");

        await AssertLacksAsync(() => PostAsync(BoardsPath(workspace.Id), new { name = "Sprint 15", visibility = "Workspace" }), "listIds");
        await AssertLacksAsync(() => GetAsync($"/api/v1/boards/{board.Id}"), "listIds");
        await AssertLacksAsync(() => UpdateBoardAsync(board.Id, "Sprint 14b"), "listIds");
        await AssertLacksAsync(() => MoveBoardAsync(board.Id, other.Id), "listIds");
        await AssertLacksAsync(() => GetAsync(BoardsPath(other.Id)), "listIds");
    }

    [Fact]
    public async Task List_reads_and_writes_carry_no_card_ids()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var other = await CreateBoardAsync(workspace.Id, "Sprint 15");
        var list = await CreateListAsync(board.Id, "To do");
        await CreateCardAsync(list.Id, "Write the brief");

        await AssertLacksAsync(() => PostAsync(ListsPath(board.Id), new { name = "Doing" }), "cardIds");
        await AssertLacksAsync(() => GetAsync($"/api/v1/lists/{list.Id}"), "cardIds");
        await AssertLacksAsync(() => UpdateListAsync(list.Id, "Backlog"), "cardIds");
        await AssertLacksAsync(() => MoveListAsync(list.Id, other.Id), "cardIds");
        await AssertLacksAsync(() => GetAsync(ListsPath(other.Id)), "cardIds");
    }

}
