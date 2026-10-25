using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private async Task<Guid> InviteIdAsync(string path, string email)
    {
        using var response = await PostAsync(path, new { email });
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
