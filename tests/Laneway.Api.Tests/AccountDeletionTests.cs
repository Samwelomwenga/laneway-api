using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class AccountDeletionTests : ApiTests
{
    public AccountDeletionTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Deleting_the_only_admin_of_places_with_other_members_is_409_per_place()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);
        await AddBoardMemberAsync(board.Id, BoardRole.Observer);

        using var response = await DeleteUserAsync(ActorUser);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var errors = await response.ReadErrors();
        Assert.Equal(
            [("workspaces", ErrorCodes.LastAdmin), ("boards", ErrorCodes.LastAdmin)],
            errors.Select(error => (error.Field, error.Code)));
        Assert.Contains("'Design'", errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("'Roadmap'", errors[1].Message, StringComparison.Ordinal);
        using var stillHere = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, stillHere.StatusCode);
    }

    [Fact]
    public async Task Being_the_only_member_does_not_block_deletion_and_the_place_stays()
    {
        var workspace = await CreateWorkspaceAsync("Solo");
        var board = await CreateBoardAsync(workspace.Id, "Notes");

        using var response = await DeleteUserAsync(ActorUser);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var left = await FromDatabaseAsync(database => Task.FromResult((
            database.Boards.Any(found => found.Id == board.Id),
            database.WorkspaceMembers.Count(member => member.WorkspaceId == workspace.Id),
            database.BoardMembers.Count(member => member.BoardId == board.Id))));
        Assert.Equal((true, 0, 0), left);
    }

    [Fact]
    public async Task Deleting_a_member_removes_every_membership_and_keeps_their_history()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var leaver = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Admin);
        using (var joined = await PostAsync(BoardMembersPath(board.Id), new { userId = leaver.Id }, leaver))
        {
            Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        }

        using var response = await DeleteUserAsync(leaver);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var workspaceMembers = await GetAsync(WorkspaceMembersPath(workspace.Id));
        Assert.Equal([ActorId], (await workspaceMembers.ReadPage<WorkspaceMemberDto>()).Entries.Select(member => member.User.Id));
        using var boardMembers = await GetAsync(BoardMembersPath(board.Id));
        Assert.Equal([ActorId], (await boardMembers.ReadPage<BoardMemberDto>()).Entries.Select(member => member.User.Id));
        Assert.Single((await ActivityAsync($"boardId={board.Id}&type=addMember")).Entries);
    }

    private Task<HttpResponseMessage> DeleteUserAsync(TestUser user) =>
        PostAsync("/api/v1/auth/delete-user", new { password = TestUser.Password }, user);
}
