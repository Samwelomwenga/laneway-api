using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class MemberAccessTests : AccessMatrix
{
    public MemberAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_workspaces_members() =>
        RunWorkspaceRowAsync(Act.ListWorkspaceMembers, 200, (place, caller) =>
            GetAsync(WorkspaceMembersPath(place.WorkspaceId), caller));

    [Fact]
    public Task POST_a_workspace_member() =>
        RunWorkspaceRowAsync(Act.AdministerWorkspace, 201, async (place, caller) =>
            await PostAsync(WorkspaceMembersPath(place.WorkspaceId), new { userId = await SeedUserAsync() }, caller));

    [Fact]
    public Task PUT_a_workspace_member() =>
        RunWorkspaceRowAsync(Act.AdministerWorkspace, 200, (place, caller) =>
            PutAsync($"{WorkspaceMembersPath(place.WorkspaceId)}/{ActorId}", new { role = "Normal" }, caller));

    [Fact]
    public Task DELETE_a_workspace_member() =>
        RunWorkspaceRowAsync(Act.AdministerWorkspace, 204, (place, caller) =>
            DeleteAsync($"{WorkspaceMembersPath(place.WorkspaceId)}/{ActorId}", caller));

    [Fact]
    public Task GET_a_boards_members() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync(BoardMembersPath(place.BoardId), caller));

    [Fact]
    public Task POST_a_board_member() =>
        RunBoardRowAsync(Act.AdministerBoard, 201, async (place, caller) =>
            await PostAsync(BoardMembersPath(place.BoardId), new { userId = await SeedUserAsync() }, caller));

    [Fact]
    public Task PUT_a_board_member() =>
        RunBoardRowAsync(Act.AdministerBoard, 200, (place, caller) =>
            PutAsync($"{BoardMembersPath(place.BoardId)}/{ActorId}", new { role = "Normal" }, caller));

    [Fact]
    public Task DELETE_a_board_member() =>
        RunBoardRowAsync(Act.AdministerBoard, 204, (place, caller) =>
            DeleteAsync($"{BoardMembersPath(place.BoardId)}/{ActorId}", caller));

    [Fact]
    public Task GET_a_boards_settings() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/boards/{place.BoardId}/settings", caller));

    [Fact]
    public Task PUT_a_boards_settings() =>
        RunBoardRowAsync(Act.AdministerBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/boards/{place.BoardId}/settings",
                new { commenting = "Disabled", addingMembers = "Members", selfJoin = false, visibility = "Public" }, caller));
}
