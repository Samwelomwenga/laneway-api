using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardAccessTests : AccessMatrix
{
    public BoardAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_board() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/boards/{place.BoardId}", caller));

    [Fact]
    public Task PUT_a_board() =>
        RunBoardRowAsync(Act.AdministerBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/boards/{place.BoardId}", new { name = "Renamed", visibility = "Public" }, caller));

    [Fact]
    public Task PUT_a_boards_archived_state() =>
        RunBoardRowAsync(Act.AdministerBoard, 204, (place, caller) =>
            PutAsync($"/api/v1/boards/{place.BoardId}/archived", new { value = true }, caller));

    [Fact]
    public Task DELETE_a_board() =>
        RunBoardRowAsync(Act.AdministerBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/boards/{place.BoardId}", caller), archived: true);

    [Fact]
    public Task PUT_a_boards_workspace() =>
        RunMoveRowAsync(Act.AdministerBoard, "workspaceId", 200, (from, to, caller) =>
            PutAsync($"/api/v1/boards/{from.BoardId}/workspace", new { workspaceId = to.WorkspaceId }, caller),
            routeObject: "Board");

    [Fact]
    public Task POST_a_board_copy() =>
        RunMoveRowAsync(Act.ReadBoard, "workspaceId", 202, (from, to, caller) =>
            PostAsync($"/api/v1/boards/{from.BoardId}/copies", new { workspaceId = to.WorkspaceId }, caller),
            routeObject: "Board");

    [Fact]
    public async Task A_guest_who_is_board_admin_moves_the_board_into_a_workspace_they_belong_to()
    {
        var guest = (await CallersAsync())[Caller.Guest];
        var place = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var theirs = NewWorkspace(WorkspaceVisibility.Private);
        await FromDatabaseAsync(async database =>
        {
            database.WorkSpaces.Add(theirs);
            AddWorkspaceMember(database, theirs.Id, guest.Id, WorkspaceRole.Normal);
            await database.BoardMembers
                .Where(member => member.BoardId == place.BoardId && member.UserId == guest.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(member => member.Role, BoardRole.Admin));
            return await database.SaveChangesAsync();
        });

        using var response = await PutAsync(
            $"/api/v1/boards/{place.BoardId}/workspace", new { workspaceId = theirs.Id }, guest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
