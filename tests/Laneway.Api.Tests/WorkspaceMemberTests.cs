using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class WorkspaceMemberTests : ApiTests
{
    public WorkspaceMemberTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Members_list_the_workspace_without_emails_and_guests_are_refused()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var normal = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);
        var guest = await AddBoardMemberAsync(board.Id, BoardRole.Normal);

        using var asNormal = await GetAsync(WorkspaceMembersPath(workspace.Id), normal);
        using var asGuest = await GetAsync(WorkspaceMembersPath(workspace.Id), guest);

        Assert.Equal(HttpStatusCode.OK, asNormal.StatusCode);
        var page = await asNormal.ReadPage<WorkspaceMemberDto>();
        Assert.Equal(
            [(ActorId, WorkspaceRole.Admin), (normal.Id, WorkspaceRole.Normal)],
            page.Entries.Select(member => (member.User.Id, member.Role)));
        Assert.DoesNotContain("email", await asNormal.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(ErrorCodes.RequiresWorkspaceMember, Assert.Single(await asGuest.ReadErrors()).Code);
    }

    [Fact]
    public async Task An_admin_adds_changes_and_removes_members_and_each_writes_an_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var user = await CreateUserAsync();

        using var added = await PostAsync(WorkspaceMembersPath(workspace.Id), new { username = user.User.Username });
        using var changed = await PutAsync($"{WorkspaceMembersPath(workspace.Id)}/{user.Id}", new { role = "Admin" });
        using var removed = await DeleteAsync($"{WorkspaceMembersPath(workspace.Id)}/{user.Id}");

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(WorkspaceRole.Normal, (await added.ReadEnvelope<WorkspaceMemberDto>()).Data!.Role);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var entries = (await ActivityAsync($"workspaceId={workspace.Id}&type=addMember,changeMemberRole,removeMember"))
            .Entries;
        Assert.Equal(
            [ActivityType.RemoveMember, ActivityType.ChangeMemberRole, ActivityType.AddMember],
            entries.Select(entry => entry.Type));
        Assert.All(entries, entry => ActivityAssert.Place(entry, workspaceId: workspace.Id));
        Assert.Equal("Normal", entries[1].Data.Text("role.old"));
        Assert.Equal("Admin", entries[1].Data.Text("role.new"));
        Assert.Equal(user.Id, entries[0].Data.Id("member.id"));
    }

    [Fact]
    public async Task Only_workspace_admins_manage_members()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var normal = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);

        using var add = await PostAsync(WorkspaceMembersPath(workspace.Id), new { userId = (await CreateUserAsync()).Id }, normal);
        using var change = await PutAsync($"{WorkspaceMembersPath(workspace.Id)}/{ActorId}", new { role = "Normal" }, normal);

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(ErrorCodes.RequiresWorkspaceAdmin, Assert.Single(await add.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
    }

    [Fact]
    public async Task Adding_an_existing_member_is_409_duplicate()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await PostAsync(WorkspaceMembersPath(workspace.Id), new { userId = ActorId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.Duplicate, Assert.Single(await response.ReadErrors()).Code);
    }

    [Fact]
    public async Task The_last_admin_cannot_leave_or_demote_while_others_remain_and_the_only_member_cannot_leave()
    {
        var alone = await CreateWorkspaceAsync("Solo");
        var shared = await CreateWorkspaceAsync("Design");
        await AddWorkspaceMemberAsync(shared.Id, WorkspaceRole.Normal);

        using var leaveAlone = await DeleteAsync($"{WorkspaceMembersPath(alone.Id)}/{ActorId}");
        using var leaveShared = await DeleteAsync($"{WorkspaceMembersPath(shared.Id)}/{ActorId}");
        using var demote = await PutAsync($"{WorkspaceMembersPath(shared.Id)}/{ActorId}", new { role = "Normal" });

        Assert.Equal(HttpStatusCode.Conflict, leaveAlone.StatusCode);
        Assert.Equal(ErrorCodes.LastMember, Assert.Single(await leaveAlone.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Conflict, leaveShared.StatusCode);
        Assert.Equal(ErrorCodes.LastAdmin, Assert.Single(await leaveShared.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(ErrorCodes.LastAdmin, Assert.Single(await demote.ReadErrors()).Code);
    }

    [Fact]
    public async Task An_admin_can_remove_another_admin()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var other = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Admin);

        using var response = await DeleteAsync($"{WorkspaceMembersPath(workspace.Id)}/{other.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Leaving_a_workspace_leaves_its_boards_with_one_remove_entry_each()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var leaver = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);
        var first = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Private);
        var second = await CreateBoardAsync(workspace.Id, "Hiring", BoardVisibility.Private);
        foreach (var board in new[] { first, second })
        {
            using var add = await PostAsync(BoardMembersPath(board.Id), new { userId = leaver.Id });
            Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        }

        using var response = await DeleteAsync($"{WorkspaceMembersPath(workspace.Id)}/{leaver.Id}", leaver);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var read = await GetAsync($"/api/v1/boards/{first.Id}", leaver);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        var entries = (await ActivityAsync($"workspaceId={workspace.Id}&type=removeMember")).Entries;
        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => ActivityAssert.Actor(entry, leaver));
        Assert.Equal(
            new Guid?[] { null, first.Id, second.Id }.Order(),
            entries.Select(entry => entry.BoardId).Order());
    }

    [Fact]
    public async Task Leaving_a_workspace_is_blocked_by_each_board_it_would_break_and_changes_nothing()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var leaver = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Admin);
        var theirs = await CreateBoardAsync(workspace.Id, "Theirs", BoardVisibility.Private);
        var shared = await CreateBoardAsync(workspace.Id, "Shared", BoardVisibility.Private);
        foreach (var board in new[] { theirs, shared })
        {
            using var add = await PostAsync(BoardMembersPath(board.Id), new { userId = leaver.Id, role = "Admin" });
            Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        }

        using (var actorLeaves = await DeleteAsync($"{BoardMembersPath(theirs.Id)}/{ActorId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, actorLeaves.StatusCode);
        }

        using (var demoted = await PutAsync($"{BoardMembersPath(shared.Id)}/{ActorId}", new { role = "Normal" }))
        {
            Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);
        }

        using var response = await DeleteAsync($"{WorkspaceMembersPath(workspace.Id)}/{leaver.Id}", leaver);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var errors = await response.ReadErrors();
        Assert.Equal(
            [("boards", ErrorCodes.LastAdmin), ("boards", ErrorCodes.LastMember)],
            errors.Select(error => (error.Field, error.Code)));
        Assert.Contains("'Shared'", errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("'Theirs'", errors[1].Message, StringComparison.Ordinal);
        using var stillMember = await GetAsync(WorkspaceMembersPath(workspace.Id), leaver);
        Assert.Equal(HttpStatusCode.OK, stillMember.StatusCode);
    }
}
