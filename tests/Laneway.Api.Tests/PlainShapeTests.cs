using System.Net;
using System.Text.Json;
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

        await AssertNoIdArrayAsync("boardIds", () => PostAsync("/api/v1/workspaces", new { name = "Ops", visibility = "Public" }));
        await AssertNoIdArrayAsync("boardIds", () => GetAsync($"/api/v1/workspaces/{workspace.Id}"));
        await AssertNoIdArrayAsync("boardIds", () => UpdateWorkspaceAsync(workspace.Id, "Design team"));
        await AssertNoIdArrayAsync("boardIds", () => GetAsync("/api/v1/workspaces"));
    }

    [Fact]
    public async Task Board_reads_and_writes_carry_no_list_ids()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var other = await CreateWorkspaceAsync("Ops");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        await CreateListAsync(board.Id, "To do");

        await AssertNoIdArrayAsync("listIds", () => PostAsync("/api/v1/boards", new { name = "Sprint 15", workspaceId = workspace.Id, visibility = "Workspace" }));
        await AssertNoIdArrayAsync("listIds", () => GetAsync($"/api/v1/boards/{board.Id}"));
        await AssertNoIdArrayAsync("listIds", () => UpdateBoardAsync(board.Id, "Sprint 14b"));
        await AssertNoIdArrayAsync("listIds", () => MoveBoardAsync(board.Id, other.Id));
        await AssertNoIdArrayAsync("listIds", () => GetAsync($"/api/v1/boards?workspaceId={other.Id}"));
    }

    [Fact]
    public async Task List_reads_and_writes_carry_no_card_ids()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var other = await CreateBoardAsync(workspace.Id, "Sprint 15");
        var list = await CreateListAsync(board.Id, "To do");
        await CreateCardAsync(list.Id, "Write the brief");

        await AssertNoIdArrayAsync("cardIds", () => PostAsync("/api/v1/lists", new { name = "Doing", boardId = board.Id }));
        await AssertNoIdArrayAsync("cardIds", () => GetAsync($"/api/v1/lists/{list.Id}"));
        await AssertNoIdArrayAsync("cardIds", () => UpdateListAsync(list.Id, "Backlog"));
        await AssertNoIdArrayAsync("cardIds", () => MoveListAsync(list.Id, other.Id));
        await AssertNoIdArrayAsync("cardIds", () => GetAsync($"/api/v1/lists?boardId={other.Id}"));
    }

    private static async Task AssertNoIdArrayAsync(string idArray, Func<Task<HttpResponseMessage>> send)
    {
        using var response = await send();
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        var items = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList() : [data];
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.False(item.Has(idArray), item.GetRawText()));
    }
}
