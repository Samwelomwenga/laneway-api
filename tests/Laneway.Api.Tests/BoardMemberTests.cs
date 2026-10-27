using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardMemberTests : ApiTests
{
    public BoardMemberTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_new_boards_member_list_holds_its_creator_as_admin_without_an_email()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await GetAsync(BoardMembersPath(board.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var row = Assert.Single(body.GetProperty("data").EnumerateArray());
        Assert.Equal(ActorId, row.Id("user.id"));
        Assert.Equal(ActorUser.User.Username, row.Text("user.username"));
        Assert.Equal(ActorUser.User.FirstName, row.Text("user.firstName"));
        Assert.Equal(ActorUser.User.LastName, row.Text("user.lastName"));
        Assert.False(row.Has("user.email"));
        Assert.Equal("Admin", row.Text("role"));
        Assert.Equal("Active", row.Text("status"));
        Assert.False(row.At("isGuest").GetBoolean());
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Adding_someone_from_outside_the_workspace_makes_them_a_normal_guest_of_the_board_only()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var outsider = await CreateUserAsync();

        using var response = await PostAsync(BoardMembersPath(board.Id), new { userId = outsider.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var added = (await response.ReadEnvelope<BoardMemberDto>()).Data!;
        Assert.Equal(outsider.Id, added.User.Id);
        Assert.Equal(BoardRole.Normal, added.Role);
        Assert.True(added.IsGuest);
        using var boardRead = await GetAsync($"/api/v1/boards/{board.Id}", outsider);
        Assert.Equal(HttpStatusCode.OK, boardRead.StatusCode);
        using var workspaceRead = await GetAsync($"/api/v1/workspaces/{workspace.Id}", outsider);
        var seen = JsonDocument.Parse(await workspaceRead.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.False(seen.Has("description"));
    }

    [Fact]
    public async Task A_member_can_be_named_by_username_or_by_email_in_any_case()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var byName = await CreateUserAsync();
        var byEmail = await CreateUserAsync();

        using var named = await PostAsync(
            BoardMembersPath(board.Id), new { username = byName.User.Username, role = "Observer" });
        using var emailed = await PostAsync(
            BoardMembersPath(board.Id), new { email = $"  {byEmail.User.Email.ToUpperInvariant()} " });

        Assert.Equal(HttpStatusCode.Created, named.StatusCode);
        Assert.Equal(BoardRole.Observer, (await named.ReadEnvelope<BoardMemberDto>()).Data!.Role);
        Assert.Equal(HttpStatusCode.Created, emailed.StatusCode);
        Assert.Equal(byEmail.Id, (await emailed.ReadEnvelope<BoardMemberDto>()).Data!.User.Id);
    }

    [Fact]
    public async Task Naming_no_user_or_more_than_one_way_is_refused_before_any_lookup()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var none = await PostAsync(BoardMembersPath(board.Id), new { role = "Normal" });
        using var both = await PostAsync(BoardMembersPath(board.Id), new { userId = ActorId, username = "someone" });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(ErrorCodes.Required, Assert.Single(await none.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(ErrorCodes.MutuallyExclusive, Assert.Single(await both.ReadErrors()).Code);
    }

    [Fact]
    public async Task An_unknown_or_deleted_user_is_400_notFound_on_the_field_that_named_them()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var deleted = await CreateUserAsync();
        using (var gone = await PostAsync("/api/v1/auth/delete-user", new { password = TestUser.Password }, deleted))
        {
            Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
        }

        using var missing = await PostAsync(BoardMembersPath(board.Id), new { username = "nobody" });
        using var removed = await PostAsync(BoardMembersPath(board.Id), new { userId = deleted.Id });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var error = Assert.Single(await missing.ReadErrors());
        Assert.Equal(("username", ErrorCodes.NotFound), (error.Field, error.Code));
        Assert.Equal(HttpStatusCode.BadRequest, removed.StatusCode);
        error = Assert.Single(await removed.ReadErrors());
        Assert.Equal(("userId", ErrorCodes.NotFound), (error.Field, error.Code));
    }

    [Fact]
    public async Task Adding_an_existing_member_is_409_duplicate()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await PostAsync(BoardMembersPath(board.Id), new { userId = ActorId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.Duplicate, Assert.Single(await response.ReadErrors()).Code);
    }

    [Fact]
    public async Task With_addingMembers_open_a_normal_member_adds_normals_and_observers_but_not_admins()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var normal = await AddBoardMemberAsync(board.Id, BoardRole.Normal);
        var observer = await AddBoardMemberAsync(board.Id, BoardRole.Observer);
        await PutBoardSettingsAsync(board.Id, addingMembers: BoardAddingMembers.Members);

        using var asNormal = await PostAsync(BoardMembersPath(board.Id), new { userId = (await CreateUserAsync()).Id }, normal);
        using var asObserverRole = await PostAsync(
            BoardMembersPath(board.Id), new { userId = (await CreateUserAsync()).Id, role = "Observer" }, normal);
        using var asAdmin = await PostAsync(
            BoardMembersPath(board.Id), new { userId = (await CreateUserAsync()).Id, role = "Admin" }, normal);
        using var byObserver = await PostAsync(BoardMembersPath(board.Id), new { userId = (await CreateUserAsync()).Id }, observer);

        Assert.Equal(HttpStatusCode.Created, asNormal.StatusCode);
        Assert.Equal(HttpStatusCode.Created, asObserverRole.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
        Assert.Equal(ErrorCodes.RequiresBoardAdmin, Assert.Single(await asAdmin.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Forbidden, byObserver.StatusCode);
        Assert.Equal(ErrorCodes.ReadOnlyRole, Assert.Single(await byObserver.ReadErrors()).Code);
    }

    [Fact]
    public async Task With_addingMembers_open_a_normal_member_still_cannot_change_or_remove_others()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var normal = await AddBoardMemberAsync(board.Id, BoardRole.Normal);
        var other = await AddBoardMemberAsync(board.Id, BoardRole.Normal);
        await PutBoardSettingsAsync(board.Id, addingMembers: BoardAddingMembers.Members);

        using var change = await PutAsync($"{BoardMembersPath(board.Id)}/{other.Id}", new { role = "Observer" }, normal);
        using var remove = await DeleteAsync($"{BoardMembersPath(board.Id)}/{other.Id}", normal);

        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(ErrorCodes.RequiresBoardAdmin, Assert.Single(await change.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        Assert.Equal(ErrorCodes.RequiresBoardAdmin, Assert.Single(await remove.ReadErrors()).Code);
    }

    [Fact]
    public async Task An_admin_changes_a_role_and_the_entry_holds_the_old_and_new_role()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var member = await AddBoardMemberAsync(board.Id, BoardRole.Normal);

        using var response = await PutAsync($"{BoardMembersPath(board.Id)}/{member.Id}", new { role = "Observer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BoardRole.Observer, (await response.ReadEnvelope<BoardMemberDto>()).Data!.Role);
        var entry = Assert.Single((await ActivityAsync($"boardId={board.Id}&type=changeMemberRole")).Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        Assert.Equal(member.Id, entry.Data.Id("member.id"));
        Assert.Equal("Normal", entry.Data.Text("role.old"));
        Assert.Equal("Observer", entry.Data.Text("role.new"));
    }

    [Fact]
    public async Task Adding_changing_and_removing_a_board_member_each_log_under_access_with_the_address()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var user = await CreateUserAsync();
        const string ip = "198.51.100.4";

        using var added = await SendFromAsync(
            ip, HttpMethod.Post, BoardMembersPath(board.Id), ActorUser, new { userId = user.Id, role = "Observer" });
        using var changed = await SendFromAsync(
            ip, HttpMethod.Put, $"{BoardMembersPath(board.Id)}/{user.Id}", ActorUser, new { role = "Normal" });
        using var removed = await SendFromAsync(ip, HttpMethod.Delete, $"{BoardMembersPath(board.Id)}/{user.Id}", ActorUser);

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent],
            [added.StatusCode, changed.StatusCode, removed.StatusCode]);
        var add = Assert.Single(AccessLogs("MemberAdded"));
        var change = Assert.Single(AccessLogs("MemberRoleChanged"));
        var remove = Assert.Single(AccessLogs("MemberRemoved"));
        foreach (var log in new[] { add, change, remove })
        {
            Assert.Equal(
                (ActorId.ToString(), "Board", board.Id.ToString(), user.Id.ToString(), ip),
                (log.GetStructuredStateValue("sub"), log.GetStructuredStateValue("targetType"),
                    log.GetStructuredStateValue("targetId"), log.GetStructuredStateValue("memberSub"),
                    log.GetStructuredStateValue(ClientIp.Field)));
        }

        Assert.Equal("Observer", add.GetStructuredStateValue("role"));
        Assert.Equal(("Normal", "Observer"), (change.GetStructuredStateValue("role"), change.GetStructuredStateValue("previousRole")));
        Assert.Equal("Normal", remove.GetStructuredStateValue("role"));
    }

    [Fact]
    public async Task Setting_the_role_a_member_has_succeeds_and_writes_nothing()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var member = await AddBoardMemberAsync(board.Id, BoardRole.Normal);

        using var response = await PutAsync($"{BoardMembersPath(board.Id)}/{member.Id}", new { role = "Normal" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ActivityAsync($"boardId={board.Id}&type=changeMemberRole")).Entries);
    }

    [Fact]
    public async Task Changing_or_removing_someone_who_is_not_a_member_is_404_member_not_found()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var outsider = await CreateUserAsync();

        using var change = await PutAsync($"{BoardMembersPath(board.Id)}/{outsider.Id}", new { role = "Normal" });
        using var remove = await DeleteAsync($"{BoardMembersPath(board.Id)}/{outsider.Id}");

        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal("Member not found", (await change.ReadEnvelope<object>()).Message);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal("Member not found", (await remove.ReadEnvelope<object>()).Message);
    }

    [Fact]
    public async Task The_last_admin_cannot_demote_themselves_remove_themselves_or_be_removed_while_others_remain()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Workspace);
        await AddBoardMemberAsync(board.Id, BoardRole.Normal);
        var workspaceAdmin = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Admin);

        using var demote = await PutAsync($"{BoardMembersPath(board.Id)}/{ActorId}", new { role = "Normal" });
        using var leave = await DeleteAsync($"{BoardMembersPath(board.Id)}/{ActorId}");
        using var removed = await DeleteAsync($"{BoardMembersPath(board.Id)}/{ActorId}", workspaceAdmin);

        foreach (var response in new[] { demote, leave, removed })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = Assert.Single(await response.ReadErrors());
            Assert.Equal(ErrorCodes.LastAdmin, error.Code);
            Assert.Equal("Make another member an admin first.", error.Message);
        }
    }

    [Fact]
    public async Task The_only_member_cannot_leave()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await DeleteAsync($"{BoardMembersPath(board.Id)}/{ActorId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(ErrorCodes.LastMember, error.Code);
        Assert.Equal("Delete this board instead.", error.Message);
    }

    [Fact]
    public async Task Any_member_can_leave_and_the_leave_is_a_remove_entry_they_wrote()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Private);
        var observer = await AddBoardMemberAsync(board.Id, BoardRole.Observer);

        using var response = await DeleteAsync($"{BoardMembersPath(board.Id)}/{observer.Id}", observer);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var after = await GetAsync($"/api/v1/boards/{board.Id}", observer);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        var entry = Assert.Single((await ActivityAsync($"boardId={board.Id}&type=removeMember")).Entries);
        ActivityAssert.Actor(entry, observer);
        Assert.Equal(observer.Id, entry.Data.Id("member.id"));
        Assert.Equal("Observer", entry.Data.Text("role"));
        var log = Assert.Single(AccessLogs("MemberRemoved"));
        Assert.Equal(
            (observer.Id.ToString(), observer.Id.ToString(), "Observer"),
            (log.GetStructuredStateValue("sub"), log.GetStructuredStateValue("memberSub"), log.GetStructuredStateValue("role")));
    }

    [Fact]
    public async Task A_second_admin_lets_the_first_step_down()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        await AddBoardMemberAsync(board.Id, BoardRole.Admin);

        using var response = await PutAsync($"{BoardMembersPath(board.Id)}/{ActorId}", new { role = "Observer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_workspace_member_self_joins_a_board_they_can_read_as_normal()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Workspace);
        var joiner = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);

        using var response = await PostAsync(BoardMembersPath(board.Id), new { userId = joiner.Id }, joiner);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var joined = (await response.ReadEnvelope<BoardMemberDto>()).Data!;
        Assert.Equal((BoardRole.Normal, false), (joined.Role, joined.IsGuest));
        var entry = Assert.Single((await ActivityAsync($"boardId={board.Id}&type=addMember")).Entries);
        ActivityAssert.Actor(entry, joiner);
        Assert.Equal(joiner.Id, entry.Data.Id("member.id"));
        var log = Assert.Single(AccessLogs("MemberAdded"), log => log.GetStructuredStateValue("targetType") == "Board");
        Assert.Equal(
            (joiner.Id.ToString(), joiner.Id.ToString(), board.Id.ToString()),
            (log.GetStructuredStateValue("sub"), log.GetStructuredStateValue("memberSub"), log.GetStructuredStateValue("targetId")));
    }

    [Fact]
    public async Task Self_join_refuses_any_role_but_normal()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Public);
        var joiner = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);

        using var asAdmin = await PostAsync(BoardMembersPath(board.Id), new { userId = joiner.Id, role = "Admin" }, joiner);
        using var asObserver = await PostAsync(
            BoardMembersPath(board.Id), new { userId = joiner.Id, role = "Observer" }, joiner);

        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asObserver.StatusCode);
    }

    [Fact]
    public async Task Self_join_is_closed_to_guests_and_strangers_and_hidden_on_private_boards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var open = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Public);
        var hidden = await CreateBoardAsync(workspace.Id, "Secrets", BoardVisibility.Private);
        var elsewhere = await CreateBoardAsync(workspace.Id, "Hiring", BoardVisibility.Private);
        var guest = await AddBoardMemberAsync(elsewhere.Id, BoardRole.Normal);
        var stranger = await CreateUserAsync();
        var colleague = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);

        using var byGuest = await PostAsync(BoardMembersPath(open.Id), new { userId = guest.Id }, guest);
        using var byStranger = await PostAsync(BoardMembersPath(open.Id), new { userId = stranger.Id }, stranger);
        using var onPrivate = await PostAsync(BoardMembersPath(hidden.Id), new { userId = colleague.Id }, colleague);

        Assert.Equal(HttpStatusCode.Forbidden, byGuest.StatusCode);
        Assert.Equal(ErrorCodes.RequiresWorkspaceMember, Assert.Single(await byGuest.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Forbidden, byStranger.StatusCode);
        Assert.Equal(ErrorCodes.RequiresWorkspaceMember, Assert.Single(await byStranger.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.NotFound, onPrivate.StatusCode);
    }

    [Fact]
    public async Task With_selfJoin_off_a_workspace_member_cannot_join_and_existing_joins_stay()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Workspace);
        var early = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);
        var late = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);
        using (var joined = await PostAsync(BoardMembersPath(board.Id), new { userId = early.Id }, early))
        {
            Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        }

        await PutBoardSettingsAsync(board.Id, selfJoin: false);
        using var response = await PostAsync(BoardMembersPath(board.Id), new { userId = late.Id }, late);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.RequiresBoardAdmin, Assert.Single(await response.ReadErrors()).Code);
        using var stillThere = await PutAsync($"/api/v1/boards/{board.Id}", new { name = "Roadmap 2" }, early);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task A_guest_added_to_the_workspace_stops_being_a_guest_and_keeps_their_board_role()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var guest = await AddBoardMemberAsync(board.Id, BoardRole.Observer);

        using var added = await PostAsync(WorkspaceMembersPath(workspace.Id), new { userId = guest.Id });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        using var list = await GetAsync(BoardMembersPath(board.Id));
        var row = Assert.Single((await list.ReadPage<BoardMemberDto>()).Entries, member => member.User.Id == guest.Id);
        Assert.Equal((BoardRole.Observer, false), (row.Role, row.IsGuest));
    }

    [Fact]
    public async Task Adding_a_member_writes_an_add_entry_on_the_board()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var added = await CreateUserAsync();

        using var response = await PostAsync(BoardMembersPath(board.Id), new { userId = added.Id, role = "Admin" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var entry = Assert.Single((await ActivityAsync($"boardId={board.Id}&type=addMember")).Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(added.Id, entry.Data.Id("member.id"));
        Assert.Equal(added.User.Username, entry.Data.Text("member.username"));
        Assert.Equal("Admin", entry.Data.Text("role"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
    }
}
