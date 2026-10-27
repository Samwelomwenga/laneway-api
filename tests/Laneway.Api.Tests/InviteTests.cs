using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class InviteTests : ApiTests
{
    public InviteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Adding_an_unknown_email_to_a_workspace_invites_it_and_emails_a_sign_up_link()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var email = NewEmail();

        using var response = await PostAsync(WorkspaceMembersPath(workspace.Id), new { email = email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await response.ReadEnvelope<JsonElement>()).Data;
        Assert.Equal("Invited", data.Text("status"));
        Assert.Equal(JsonValueKind.Null, data.At("user").ValueKind);
        Assert.Equal(email, data.Text("email"));
        Assert.NotEqual(Guid.Empty, data.Id("inviteId"));
        var send = await AwaitEmailAsync(email);
        Assert.Equal("You're invited to a Laneway workspace", send.Subject);
        Assert.Equal(["InviteSent"], send.Tags);
        Assert.Contains($"{ApiFactory.AllowedOrigin}/sign-up#email={Uri.EscapeDataString(email)}\n", send.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unverified_users_address_gets_a_board_invite_with_no_guest_flag()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var email = NewEmail();
        using (var signedUp = await SignUpAsync(SignUpBody(email, username: "squatter")))
        {
            Assert.Equal(HttpStatusCode.Accepted, signedUp.StatusCode);
        }

        using var response = await PostAsync(BoardMembersPath(board.Id), new { email, role = "Observer" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await response.ReadEnvelope<JsonElement>()).Data;
        Assert.Equal(("Invited", "Observer"), (data.Text("status"), data.Text("role")));
        Assert.Equal(JsonValueKind.Null, data.At("isGuest").ValueKind);
        var send = await Brevo.AwaitEmailAsync(email, "InviteSent");
        Assert.Equal("You're invited to a Laneway board", send.Subject);
    }

    [Fact]
    public async Task Inviting_needs_what_adding_needs()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var normal = await AddBoardMemberAsync(board.Id, BoardRole.Normal);
        var observer = await AddBoardMemberAsync(board.Id, BoardRole.Observer);
        var workspaceNormal = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);

        using var asAdmin = await PostAsync(BoardMembersPath(board.Id), new { email = NewEmail(), role = "Admin" }, normal);
        using var byObserver = await PostAsync(BoardMembersPath(board.Id), new { email = NewEmail() }, observer);
        using var toWorkspace = await PostAsync(WorkspaceMembersPath(workspace.Id), new { email = NewEmail() }, workspaceNormal);

        Assert.Equal(ErrorCodes.RequiresBoardAdmin, Assert.Single(await asAdmin.ReadErrors()).Code);
        Assert.Equal(ErrorCodes.ReadOnlyRole, Assert.Single(await byObserver.ReadErrors()).Code);
        Assert.Equal(ErrorCodes.RequiresWorkspaceAdmin, Assert.Single(await toWorkspace.ReadErrors()).Code);
        Assert.Equal(0, await FromDatabaseAsync(database => database.Invites.CountAsync()));
    }

    [Fact]
    public async Task A_repeat_invite_renews_the_30_days_and_emails_again_only_after_24_hours()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var email = NewEmail();
        var first = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email);
        await Brevo.AwaitEmailAsync(email, "InviteSent");
        Time.Advance(TimeSpan.FromMinutes(1));

        var renewed = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email);
        await MoveInviteBackAsync(first, Invites.ResendAfter);
        var resent = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email);

        Assert.Equal([first, first], [renewed, resent]);
        await Brevo.AwaitEmailAsync(email, "InviteSent", nth: 2);
        Assert.Equal(2, await FromDatabaseAsync(database =>
            database.Deliveries.CountAsync(d => d.Type == MessageType.InviteSent)));
        var invite = await FromDatabaseAsync(database => database.Invites.SingleAsync());
        Assert.Equal(Time.GetUtcNow().UtcDateTime + Invites.Lifetime, invite.ExpiresAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_repeat_invite_with_another_role_is_409_duplicate()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var email = NewEmail();
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email);

        using var response = await PostAsync(WorkspaceMembersPath(workspace.Id), new { email, role = "Admin" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.Duplicate, Assert.Single(await response.ReadErrors()).Code);
        Assert.Equal("Normal", (await FromDatabaseAsync(database => database.Invites.SingleAsync())).Role);
    }

    [Fact]
    public async Task The_51st_invite_email_from_one_user_in_24_hours_is_429()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var other = await CreateUserAsync();
        await SeedInviteEmailsAsync(ActorId, Invites.DailyEmails - 1);
        await SeedInviteEmailsAsync(other.Id, 5);
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), NewEmail());

        using var response = await PostAsync(WorkspaceMembersPath(workspace.Id), new { email = NewEmail() });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(ErrorCodes.TooManyRequests, Assert.Single(await response.ReadErrors()).Code);
        Assert.Equal(1, await FromDatabaseAsync(database => database.Invites.CountAsync()));
    }

    [Fact]
    public async Task Deleting_a_place_deletes_its_invites_and_a_board_move_keeps_them()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var target = await CreateWorkspaceAsync("Platform");
        var moved = await CreateBoardAsync(workspace.Id, "Roadmap");
        var deleted = await CreateBoardAsync(workspace.Id, "Retro");
        var movedInvite = await InviteIdAsync(BoardMembersPath(moved.Id), NewEmail());
        await InviteIdAsync(BoardMembersPath(deleted.Id), NewEmail());
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), NewEmail());

        using var move = await MoveBoardAsync(moved.Id, target.Id);
        await SetBoardArchivedAsync(deleted.Id, true);
        using var deleteBoard = await DeleteAsync($"/api/v1/boards/{deleted.Id}");
        using var deleteWorkspace = await DeleteAsync($"/api/v1/workspaces/{workspace.Id}");

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NoContent],
            [move.StatusCode, deleteBoard.StatusCode, deleteWorkspace.StatusCode]);
        Assert.Equal([movedInvite], await FromDatabaseAsync(database => database.Invites.Select(i => i.Id).ToListAsync()));
    }

    [Fact]
    public async Task Verifying_the_address_makes_a_workspace_invite_a_membership_added_by_the_inviter()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var email = NewEmail();
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email, role: "Admin");

        var joined = await SignUpAndVerifyAsync(email);

        using var members = await GetAsync(WorkspaceMembersPath(workspace.Id));
        Assert.Equal(
            [(ActorId, WorkspaceRole.Admin), (joined, WorkspaceRole.Admin)],
            (await members.ReadPage<WorkspaceMemberDto>()).Entries.Select(member => (member.User.Id, member.Role)));
        var entry = Assert.Single((await ActivityAsync($"workspaceId={workspace.Id}&type=addMember")).Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(joined, entry.Data.Id("member.id"));
        Assert.False(await FromDatabaseAsync(database => database.Invites.AnyAsync()));
    }

    [Fact]
    public async Task A_converted_invite_logs_member_added_by_the_inviter_in_the_invitees_request()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var inviter = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Admin);
        var email = NewEmail();
        using (var invited = await PostAsync(WorkspaceMembersPath(workspace.Id), new { email, role = "Admin" }, inviter))
        {
            Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        }

        var workspaceInvite = await FromDatabaseAsync(database => database.Invites.Select(i => i.Id).SingleAsync());
        var boardInvite = await InviteIdAsync(BoardMembersPath(board.Id), email, role: "Observer");
        const string ip = "2001:db8::51";

        var joined = await SignUpAndVerifyAsync(email, ip);

        var verify = ServerSpan("api/v1/auth/verify-email");
        var added = AccessLogs("MemberAdded").Where(log => log.GetStructuredStateValue("memberSub") == joined.ToString());
        Assert.Equal(
            [
                (inviter.Id.ToString(), "Workspace", workspace.Id.ToString(), "Admin", workspaceInvite.ToString(), ip),
                (ActorId.ToString(), "Board", board.Id.ToString(), "Observer", boardInvite.ToString(), ip)
            ],
            added.Select(log => (
                log.GetStructuredStateValue("sub"),
                log.GetStructuredStateValue("targetType"),
                log.GetStructuredStateValue("targetId"),
                log.GetStructuredStateValue("role"),
                log.GetStructuredStateValue("inviteId"),
                log.GetStructuredStateValue(ClientIp.Field))).OrderByDescending(row => row.Item2, StringComparer.Ordinal));
        var shipped = ShippedLogs.Where(log => log.EventId.Name == "MemberAdded"
            && log.Attributes!.Any(pair => pair.Key == "memberSub" && Equals(pair.Value, joined))).ToList();
        Assert.Equal(2, shipped.Count);
        Assert.All(shipped, log => Assert.Equal(verify.TraceId, log.TraceId));
        Assert.Empty(AccessLogs("InviteRevoked"));
    }

    [Fact]
    public async Task A_confirmed_email_change_logs_the_invite_it_converts()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var user = await CreateUserAsync();
        var email = NewEmail();
        var invite = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email);

        await ChangeEmailAsync(user, email);

        var log = Assert.Single(AccessLogs("MemberAdded"), log => log.GetStructuredStateValue("memberSub") == user.Id.ToString());
        Assert.Equal(
            (ActorId.ToString(), workspace.Id.ToString(), "Normal", invite.ToString()),
            (log.GetStructuredStateValue("sub"), log.GetStructuredStateValue("targetId"),
                log.GetStructuredStateValue("role"), log.GetStructuredStateValue("inviteId")));
        Assert.Empty(AccessLogs("InviteRevoked"));
    }

    [Fact]
    public async Task A_board_invite_makes_someone_outside_the_workspace_a_guest()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var email = NewEmail();
        await InviteIdAsync(BoardMembersPath(board.Id), email, role: "Observer");

        var joined = await SignUpAndVerifyAsync(email);

        using var members = await GetAsync(BoardMembersPath(board.Id));
        var member = Assert.Single(
            (await members.ReadPage<BoardMemberDto>()).Entries, member => member.User.Id == joined);
        Assert.Equal((BoardRole.Observer, true), (member.Role, member.IsGuest));
        var entry = Assert.Single((await ActivityAsync($"boardId={board.Id}&type=addMember")).Entries,
            entry => entry.Data.Id("member.id") == joined);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
    }

    [Fact]
    public async Task Signing_up_with_another_address_converts_nothing()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), NewEmail());

        await SignUpAndVerifyAsync(NewEmail());

        using var members = await GetAsync(WorkspaceMembersPath(workspace.Id));
        Assert.Equal([ActorId], (await members.ReadPage<WorkspaceMemberDto>()).Entries.Select(member => member.User.Id));
        Assert.Equal(1, await FromDatabaseAsync(database => database.Invites.CountAsync()));
    }

    [Fact]
    public async Task An_invite_converts_after_its_inviter_is_removed_and_deleted()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var inviter = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Admin);
        var email = NewEmail();
        using (var invited = await PostAsync(WorkspaceMembersPath(workspace.Id), new { email }, inviter))
        {
            Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        }

        using var removed = await DeleteAsync($"{WorkspaceMembersPath(workspace.Id)}/{inviter.Id}");
        using var deleted = await PostAsync("/api/v1/auth/delete-user", new { password = TestUser.Password }, inviter);
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent], [removed.StatusCode, deleted.StatusCode]);

        var joined = await SignUpAndVerifyAsync(email);

        var entry = Assert.Single((await ActivityAsync($"workspaceId={workspace.Id}&type=addMember")).Entries,
            entry => entry.Data.Id("member.id") == joined);
        ActivityAssert.Actor(entry, inviter);
    }

    [Fact]
    public async Task Confirming_a_change_to_the_address_converts_and_undoing_it_keeps_the_membership()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var user = await CreateUserAsync();
        var email = NewEmail();
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email);

        await ChangeEmailAsync(user, email);
        var undoToken = (await Brevo.AwaitEmailAsync(user.User.Email, "EmailChanged")).TokenFrom(AppRoutes.UndoEmailChange);
        using var undo = await SendAnonymousAsync(HttpMethod.Post, "/api/v1/auth/undo-email-change",
            new { token = undoToken, recentPassword = TestUser.Password, newPassword = "quiet harbor lantern" });

        Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);
        using var members = await GetAsync(WorkspaceMembersPath(workspace.Id));
        Assert.Equal(
            [(ActorId, WorkspaceRole.Admin), (user.Id, WorkspaceRole.Normal)],
            (await members.ReadPage<WorkspaceMemberDto>()).Entries.Select(member => (member.User.Id, member.Role)));
        Assert.False(await FromDatabaseAsync(database => database.Invites.AnyAsync()));
    }

    [Fact]
    public async Task Verifying_converts_every_live_invite_for_the_address_and_skips_an_expired_one()
    {
        var design = await CreateWorkspaceAsync("Design");
        var platform = await CreateWorkspaceAsync("Platform");
        var archive = await CreateWorkspaceAsync("Archive");
        var email = NewEmail();
        await InviteIdAsync(WorkspaceMembersPath(design.Id), email);
        await InviteIdAsync(WorkspaceMembersPath(platform.Id), email);
        var expired = await InviteIdAsync(WorkspaceMembersPath(archive.Id), email);
        await MoveInviteBackAsync(expired, Invites.Lifetime);

        var joined = await SignUpAndVerifyAsync(email);

        Assert.Equal(
            new[] { design.Id, platform.Id }.Order(),
            await FromDatabaseAsync(database => database.WorkspaceMembers
                .Where(member => member.UserId == joined)
                .Select(member => member.WorkspaceId)
                .Order()
                .ToListAsync()));
        Assert.Equal([expired], await FromDatabaseAsync(database => database.Invites.Select(i => i.Id).ToListAsync()));
    }

    [Fact]
    public async Task An_invite_for_someone_already_a_member_is_deleted_and_changes_nothing()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var member = await AddWorkspaceMemberAsync(workspace.Id, WorkspaceRole.Normal);
        using (var onBoard = await PostAsync(BoardMembersPath(board.Id), new { userId = member.Id, role = "Observer" }))
        {
            Assert.Equal(HttpStatusCode.Created, onBoard.StatusCode);
        }

        var email = NewEmail();
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), email, role: "Admin");
        await InviteIdAsync(BoardMembersPath(board.Id), email, role: "Admin");

        await ChangeEmailAsync(member, email);

        using var workspaceMembers = await GetAsync(WorkspaceMembersPath(workspace.Id));
        Assert.Equal(
            [(ActorId, WorkspaceRole.Admin), (member.Id, WorkspaceRole.Normal)],
            (await workspaceMembers.ReadPage<WorkspaceMemberDto>()).Entries.Select(entry => (entry.User.Id, entry.Role)));
        using var boardMembers = await GetAsync(BoardMembersPath(board.Id));
        Assert.Equal(
            BoardRole.Observer,
            Assert.Single((await boardMembers.ReadPage<BoardMemberDto>()).Entries, entry => entry.User.Id == member.Id).Role);
        Assert.Equal(2, (await ActivityAsync($"workspaceId={workspace.Id}&type=addMember")).Entries.Count);
        Assert.False(await FromDatabaseAsync(database => database.Invites.AnyAsync()));
    }

    [Fact]
    public async Task A_workspace_lists_its_live_invites_and_its_members_list_shows_none()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var other = await CreateWorkspaceAsync("Platform");
        var first = NewEmail();
        var second = NewEmail();
        var firstId = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), first, role: "Admin");
        Time.Advance(TimeSpan.FromMinutes(1));
        var secondId = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), second);
        var expired = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), NewEmail());
        await MoveInviteBackAsync(expired, Invites.Lifetime);
        await InviteIdAsync(WorkspaceMembersPath(other.Id), NewEmail());

        using var response = await GetAsync(WorkspaceInvitesPath(workspace.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var invites = (await response.ReadPage<WorkspaceInviteDto>()).Entries;
        Assert.Equal(
            [(firstId, first, WorkspaceRole.Admin), (secondId, second, WorkspaceRole.Normal)],
            invites.Select(invite => (invite.Id, invite.Email, invite.Role)));
        Assert.All(invites, invite => Assert.Equal(ActorId, invite.InvitedBy!.Id));
        Assert.Equal(invites[1].CreatedAt + Invites.Lifetime, invites[1].ExpiresAt, TimeSpan.FromMilliseconds(1));
        using var members = await GetAsync(WorkspaceMembersPath(workspace.Id));
        Assert.Equal([ActorId], (await members.ReadPage<WorkspaceMemberDto>()).Entries.Select(member => member.User.Id));
    }

    [Fact]
    public async Task Revoking_a_board_invite_deletes_it_and_an_expired_or_unknown_one_is_404()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var other = await CreateBoardAsync(workspace.Id, "Retro");
        var revoked = await InviteIdAsync(BoardMembersPath(board.Id), NewEmail());
        var expired = await InviteIdAsync(BoardMembersPath(board.Id), NewEmail());
        await MoveInviteBackAsync(expired, Invites.Lifetime);
        var elsewhere = await InviteIdAsync(BoardMembersPath(other.Id), NewEmail());

        using var revoke = await DeleteAsync($"{BoardInvitesPath(board.Id)}/{revoked}");
        using var again = await DeleteAsync($"{BoardInvitesPath(board.Id)}/{revoked}");
        using var late = await DeleteAsync($"{BoardInvitesPath(board.Id)}/{expired}");
        using var wrongPlace = await DeleteAsync($"{BoardInvitesPath(board.Id)}/{elsewhere}");

        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
            [revoke.StatusCode, again.StatusCode, late.StatusCode, wrongPlace.StatusCode]);
        Assert.Equal(
            new[] { expired, elsewhere }.Order(),
            await FromDatabaseAsync(database => database.Invites.Select(i => i.Id).Order().ToListAsync()));
        using var listed = await GetAsync(BoardInvitesPath(board.Id));
        Assert.Empty((await listed.ReadPage<BoardInviteDto>()).Entries);
        using var members = await GetAsync(BoardMembersPath(other.Id));
        Assert.Equal([ActorId], (await members.ReadPage<BoardMemberDto>()).Entries.Select(member => member.User.Id));
    }

    [Fact]
    public async Task Inviting_and_revoking_log_under_access_with_the_invite_id_and_never_the_email()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var email = NewEmail();
        const string ip = "192.0.2.44";

        using var invited = await SendFromAsync(
            ip, HttpMethod.Post, WorkspaceMembersPath(workspace.Id), ActorUser, new { email, role = "Admin" });
        using var renewed = await SendFromAsync(
            ip, HttpMethod.Post, WorkspaceMembersPath(workspace.Id), ActorUser, new { email, role = "Admin" });
        var boardInvite = await InviteIdAsync(BoardMembersPath(board.Id), email, role: "Observer");
        using var revoked = await SendFromAsync(
            ip, HttpMethod.Delete, $"{BoardInvitesPath(board.Id)}/{boardInvite}", ActorUser);
        using var missing = await DeleteAsync($"{BoardInvitesPath(board.Id)}/{boardInvite}");

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.NotFound],
            [invited.StatusCode, renewed.StatusCode, revoked.StatusCode, missing.StatusCode]);
        var workspaceInvite = (await invited.ReadEnvelope<JsonElement>()).Data.Id("inviteId");
        var sent = AccessLogs("InviteSent");
        Assert.Equal(
            [
                ("Workspace", workspace.Id.ToString(), workspaceInvite.ToString(), "Admin", ip),
                ("Workspace", workspace.Id.ToString(), workspaceInvite.ToString(), "Admin", ip),
                ("Board", board.Id.ToString(), boardInvite.ToString(), "Observer", null)
            ],
            sent.Select(log => (
                log.GetStructuredStateValue("targetType"),
                log.GetStructuredStateValue("targetId"),
                log.GetStructuredStateValue("inviteId"),
                log.GetStructuredStateValue("role"),
                log.GetStructuredStateValue(ClientIp.Field))));
        var revoke = Assert.Single(AccessLogs("InviteRevoked"));
        Assert.Equal(
            (ActorId.ToString(), "Board", board.Id.ToString(), boardInvite.ToString(), "Observer", ip),
            (revoke.GetStructuredStateValue("sub"), revoke.GetStructuredStateValue("targetType"),
                revoke.GetStructuredStateValue("targetId"), revoke.GetStructuredStateValue("inviteId"),
                revoke.GetStructuredStateValue("role"), revoke.GetStructuredStateValue(ClientIp.Field)));
        Assert.All(sent, log => Assert.Equal(ActorId.ToString(), log.GetStructuredStateValue("sub")));
        Assert.DoesNotContain(Logs(), log =>
            log.Message.Contains(email, StringComparison.OrdinalIgnoreCase)
            || log.StructuredState?.Any(pair => pair.Value?.Contains(email, StringComparison.OrdinalIgnoreCase) == true) == true);
    }

    [Fact]
    public async Task The_sweeper_deletes_an_invite_30_days_after_its_last_resend()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var stale = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), NewEmail());
        var renewed = NewEmail();
        var kept = await InviteIdAsync(WorkspaceMembersPath(workspace.Id), renewed);
        var sweeper = Services.GetRequiredService<InviteSweeper>();

        await MoveInviteBackAsync(stale, TimeSpan.FromDays(10));
        await MoveInviteBackAsync(kept, TimeSpan.FromDays(10));
        await InviteIdAsync(WorkspaceMembersPath(workspace.Id), renewed);
        await MoveInviteBackAsync(stale, TimeSpan.FromDays(20) - TimeSpan.FromMinutes(1));
        await MoveInviteBackAsync(kept, TimeSpan.FromDays(20) - TimeSpan.FromMinutes(1));
        Assert.Equal(0, await sweeper.SweepAsync(CancellationToken.None));
        await MoveInviteBackAsync(stale, TimeSpan.FromMinutes(2));
        await MoveInviteBackAsync(kept, TimeSpan.FromMinutes(2));

        Assert.Equal(1, await sweeper.SweepAsync(CancellationToken.None));
        Assert.Equal([kept], await FromDatabaseAsync(database => database.Invites.Select(i => i.Id).ToListAsync()));
    }

    [Fact]
    public async Task The_unsubscribe_link_drops_later_invites_and_they_still_read_invited()
    {
        var design = await CreateWorkspaceAsync("Design");
        var platform = await CreateWorkspaceAsync("Platform");
        var email = NewEmail();
        await InviteIdAsync(WorkspaceMembersPath(design.Id), email);
        var token = (await Brevo.AwaitEmailAsync(email, "InviteSent")).TokenFrom(AppRoutes.UnsubscribeInvites);

        using var unsubscribe = await UnsubscribeAsync(token);
        using var again = await UnsubscribeAsync(token);

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent], [unsubscribe.StatusCode, again.StatusCode]);
        var suppression = await FromDatabaseAsync(database => database.Suppressions.SingleAsync());
        Assert.Equal(Suppressions.Hash(email), suppression.AddressHash);
        Assert.Equal(SuppressionKind.Unsubscribed, suppression.Kind);
        using var later = await PostAsync(WorkspaceMembersPath(platform.Id), new { email });
        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
        Assert.Equal("Invited", (await later.ReadEnvelope<JsonElement>()).Data.Text("status"));
        await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        Assert.Single(Brevo.Sends, send => send.To == email);
    }

    [Fact]
    public async Task Each_unsubscribe_logs_whether_the_address_was_already_suppressed_and_never_the_address()
    {
        var design = await CreateWorkspaceAsync("Design");
        var email = NewEmail();
        await InviteIdAsync(WorkspaceMembersPath(design.Id), email);
        var token = (await Brevo.AwaitEmailAsync(email, "InviteSent")).TokenFrom(AppRoutes.UnsubscribeInvites);

        using var unsubscribe = await SendFromAsync("198.51.100.8", HttpMethod.Post, "/api/v1/invites/unsubscribe", body: new { token });
        using var again = await SendFromAsync("198.51.100.8", HttpMethod.Post, "/api/v1/invites/unsubscribe", body: new { token });

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent], [unsubscribe.StatusCode, again.StatusCode]);
        var logs = LogsOf(MessageLog.Category, "InvitesUnsubscribed");
        Assert.Equal(["False", "True"], logs.Select(log => log.GetStructuredStateValue("alreadySuppressed")));
        Assert.All(logs, log =>
        {
            Assert.Null(log.GetStructuredStateValue(ClientIp.Field));
            Assert.DoesNotContain(email, log.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task A_bad_unsubscribe_token_is_400_invalid_token()
    {
        using var response = await UnsubscribeAsync("not-a-protected-payload");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidToken, Assert.Single(await response.ReadErrors()).Code);
        Assert.False(await FromDatabaseAsync(database => database.Suppressions.AnyAsync()));
        Assert.Empty(LogsOf(MessageLog.Category, "InvitesUnsubscribed"));
    }

    private Task<HttpResponseMessage> UnsubscribeAsync(string token) =>
        SendAnonymousAsync(HttpMethod.Post, "/api/v1/invites/unsubscribe", new { token });

    private async Task ChangeEmailAsync(TestUser user, string newEmail)
    {
        using (var change = await PostAsync(
                   "/api/v1/auth/change-email", new { password = TestUser.Password, newEmail }, user))
        {
            Assert.Equal(HttpStatusCode.Accepted, change.StatusCode);
        }

        var send = await Brevo.AwaitEmailAsync(newEmail, "ConfirmEmailChange");
        using var confirm = await SendAnonymousAsync(HttpMethod.Post, "/api/v1/auth/confirm-email-change",
            new { token = send.TokenFrom(AppRoutes.ConfirmEmailChange) });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
    }

    private async Task<Guid> SignUpAndVerifyAsync(string email, string? clientIp = null)
    {
        using (var signUp = await SignUpAsync(SignUpBody(email)))
        {
            Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);
        }

        var send = await Brevo.AwaitEmailAsync(email, "VerifyEmail");
        using var verify = await VerifyEmailAsync(send.TokenFrom(AppRoutes.VerifyEmail), clientIp);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        return await FromDatabaseAsync(database => database.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
    }

    private async Task<Guid> InviteIdAsync(string path, string email, string role = "Normal")
    {
        using var response = await PostAsync(path, new { email, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<JsonElement>()).Data.Id("inviteId");
    }

    private Task<int> MoveInviteBackAsync(Guid id, TimeSpan by) =>
        FromDatabaseAsync(database => database.Invites.Where(invite => invite.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(invite => invite.CreatedAt, invite => invite.CreatedAt - by)
            .SetProperty(invite => invite.EmailedAt, invite => invite.EmailedAt - by)
            .SetProperty(invite => invite.ExpiresAt, invite => invite.ExpiresAt - by)));

    private async Task SeedInviteEmailsAsync(Guid inviterId, int count)
    {
        await using var scope = Services.CreateAsyncScope();
        var messages = scope.ServiceProvider.GetRequiredService<Messages>();
        for (var i = 0; i < count; i++)
        {
            messages.Add(new InviteSent(NewEmail(), inviterId, "Ana Waweru", "ana", InvitePlace.Workspace, "Design"));
        }

        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
    }
}
