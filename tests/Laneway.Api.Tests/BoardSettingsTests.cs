using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardSettingsTests : ApiTests
{
    public BoardSettingsTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_new_board_has_the_default_settings()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Public);

        var settings = await ReadSettingsAsync(board.Id);

        Assert.Equal(new BoardSettingsDto(BoardCommenting.Members, BoardAddingMembers.Admins, true, BoardVisibility.Public), settings);
    }

    [Fact]
    public async Task A_board_created_with_no_settings_is_private()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await PostAsync(BoardsPath(workspace.Id), new { name = "Roadmap" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(BoardVisibility.Private, (await response.ReadEnvelope<BoardDto>()).Data!.Visibility);
    }

    [Fact]
    public async Task A_board_can_be_created_with_its_settings()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await PostAsync(BoardsPath(workspace.Id), new
        {
            name = "Roadmap",
            visibility = "Workspace",
            commenting = "Disabled",
            addingMembers = "Members",
            selfJoin = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var board = (await response.ReadEnvelope<BoardDto>()).Data!;
        Assert.Equal(
            new BoardSettingsDto(BoardCommenting.Disabled, BoardAddingMembers.Members, false, BoardVisibility.Workspace),
            await ReadSettingsAsync(board.Id));
    }

    [Fact]
    public async Task PUT_replaces_every_setting_and_writes_the_old_values_that_changed()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Private);

        using var response = await PutAsync(SettingsPath(board.Id), new
        {
            commenting = "Observers",
            addingMembers = "Admins",
            selfJoin = false,
            visibility = "Public"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = new BoardSettingsDto(BoardCommenting.Observers, BoardAddingMembers.Admins, false, BoardVisibility.Public);
        Assert.Equal(expected, (await response.ReadEnvelope<BoardSettingsDto>()).Data);
        Assert.Equal(expected, await ReadSettingsAsync(board.Id));
        Assert.Equal(BoardVisibility.Public, (await ReadBoardAsync(board.Id)).Visibility);

        var entry = Assert.Single((await ActivityAsync($"boardId={board.Id}&type=updateBoardSettings")).Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Members", entry.Data.Text("old.commenting"));
        Assert.True(entry.Data.At("old.selfJoin").GetBoolean());
        Assert.Equal("Private", entry.Data.Text("old.visibility"));
        Assert.False(entry.Data.Has("old.addingMembers"));
    }

    [Fact]
    public async Task A_PUT_that_changes_nothing_writes_no_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Workspace);

        using var response = await PutAsync(SettingsPath(board.Id), new
        {
            commenting = "Members",
            addingMembers = "Admins",
            selfJoin = true,
            visibility = "Workspace"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ActivityAsync($"boardId={board.Id}&type=updateBoardSettings")).Entries);
    }

    [Fact]
    public async Task PUT_needs_every_setting()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await PutAsync(SettingsPath(board.Id), new { selfJoin = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["addingMembers", "commenting", "visibility"],
            (await response.ReadErrors()).Select(error => error.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_archived_boards_settings_read_but_do_not_change()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        await SetBoardArchivedAsync(board.Id, true);

        using var response = await PutAsync(SettingsPath(board.Id), new
        {
            commenting = "Disabled",
            addingMembers = "Admins",
            selfJoin = true,
            visibility = "Workspace"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.Archived, Assert.Single(await response.ReadErrors()).Code);
        Assert.Equal(BoardCommenting.Members, (await ReadSettingsAsync(board.Id)).Commenting);
    }

    [Fact]
    public async Task PUT_on_a_board_no_longer_takes_visibility()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await PutAsync($"/api/v1/boards/{board.Id}", new { name = "Roadmap", visibility = "Public" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(("visibility", ErrorCodes.UnknownField), (error.Field, error.Code));
    }

    private static string SettingsPath(Guid boardId) => $"/api/v1/boards/{boardId}/settings";

    private async Task<BoardSettingsDto> ReadSettingsAsync(Guid boardId)
    {
        using var response = await GetAsync(SettingsPath(boardId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<BoardSettingsDto>()).Data!;
    }
}
