using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CommentRuleTests : AccessMatrix
{
    public CommentRuleTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Disabled_commenting_refuses_board_admins_and_workspace_admins_too()
    {
        var place = await PlaceAsync(BoardVisibility.Workspace, BoardCommenting.Disabled);

        foreach (var caller in new[] { Caller.BoardAdmin, Caller.WorkspaceAdmin })
        {
            using var response = await CommentAsync(place, caller);

            await AssertRefusedAsync(response, ErrorCodes.CommentingDisabled);
        }
    }

    [Fact]
    public async Task Observers_commenting_lets_observers_comment_but_not_readers_off_the_board()
    {
        var place = await PlaceAsync(BoardVisibility.Workspace, BoardCommenting.Observers);

        using var observer = await CommentAsync(place, Caller.BoardObserver);
        using var reader = await CommentAsync(place, Caller.WorkspaceNormal);

        Assert.Equal(HttpStatusCode.Created, observer.StatusCode);
        await AssertRefusedAsync(reader, ErrorCodes.RequiresBoardMember);
    }

    [Fact]
    public async Task Workspace_commenting_adds_workspace_members_who_read_the_board()
    {
        var place = await PlaceAsync(BoardVisibility.Public, BoardCommenting.Workspace);

        using var workspaceNormal = await CommentAsync(place, Caller.WorkspaceNormal);
        using var guest = await CommentAsync(place, Caller.Guest);
        using var stranger = await CommentAsync(place, Caller.Stranger);

        Assert.Equal(HttpStatusCode.Created, workspaceNormal.StatusCode);
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        await AssertRefusedAsync(stranger, ErrorCodes.RequiresWorkspaceMember);
    }

    [Fact]
    public async Task Public_commenting_lets_anyone_who_reads_the_board_comment()
    {
        var place = await PlaceAsync(BoardVisibility.Public, BoardCommenting.Public);

        using var stranger = await CommentAsync(place, Caller.Stranger);

        Assert.Equal(HttpStatusCode.Created, stranger.StatusCode);
    }

    [Fact]
    public async Task Public_commenting_still_hides_a_board_the_caller_cant_read()
    {
        var place = await PlaceAsync(BoardVisibility.Private, BoardCommenting.Public);

        using var stranger = await CommentAsync(place, Caller.Stranger);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
    }

    [Fact]
    public async Task A_board_admin_deletes_anyones_comment_but_never_edits_it()
    {
        var place = await PlaceAsync(BoardVisibility.Workspace, BoardCommenting.Members);
        var callers = await CallersAsync();
        var admin = callers[Caller.BoardAdmin];
        var othersComment = place.Content.CommentIds[callers[Caller.BoardNormal].Id];
        var otherAdminsComment = place.Content.CommentIds[callers[Caller.WorkspaceAdmin].Id];

        using var edited = await PutAsync($"/api/v1/comments/{othersComment}", new { text = "Mine now." }, admin);
        using var removed = await DeleteAsync($"/api/v1/comments/{othersComment}", admin);
        using var removedAdmins = await DeleteAsync($"/api/v1/comments/{otherAdminsComment}", admin);

        await AssertRefusedAsync(edited, ErrorCodes.NotAuthor);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedAdmins.StatusCode);
    }

    [Fact]
    public async Task A_workspace_admin_deletes_comments_on_a_board_they_get_admin_rights_on()
    {
        var place = await PlaceAsync(BoardVisibility.Workspace, BoardCommenting.Members);
        var callers = await CallersAsync();
        var comment = place.Content.CommentIds[callers[Caller.BoardNormal].Id];

        using var removed = await DeleteAsync($"/api/v1/comments/{comment}", callers[Caller.WorkspaceAdmin]);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    [Fact]
    public async Task A_board_admin_deletes_comments_while_commenting_is_disabled()
    {
        var place = await PlaceAsync(BoardVisibility.Private, BoardCommenting.Disabled);
        var callers = await CallersAsync();
        var comment = place.Content.CommentIds[callers[Caller.BoardNormal].Id];

        using var removed = await DeleteAsync($"/api/v1/comments/{comment}", callers[Caller.BoardAdmin]);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    [Fact]
    public async Task An_author_loses_their_comment_once_they_can_no_longer_comment()
    {
        var place = await PlaceAsync(BoardVisibility.Private, BoardCommenting.Disabled);
        var callers = await CallersAsync();
        var author = callers[Caller.BoardNormal];
        var comment = place.Content.CommentIds[author.Id];

        using var edited = await PutAsync($"/api/v1/comments/{comment}", new { text = "Edited." }, author);
        using var removed = await DeleteAsync($"/api/v1/comments/{comment}", author);

        await AssertRefusedAsync(edited, ErrorCodes.CommentingDisabled);
        await AssertRefusedAsync(removed, ErrorCodes.CommentingDisabled);
    }

    [Fact]
    public async Task A_removed_author_reads_their_comment_as_a_stranger_would()
    {
        var place = await PlaceAsync(BoardVisibility.Private, BoardCommenting.Members);
        var callers = await CallersAsync();
        var author = callers[Caller.Guest];
        var comment = place.Content.CommentIds[author.Id];
        await FromDatabaseAsync(database => database.BoardMembers
            .Where(member => member.BoardId == place.BoardId && member.UserId == author.Id)
            .ExecuteDeleteAsync());

        using var edited = await PutAsync($"/api/v1/comments/{comment}", new { text = "Edited." }, author);
        using var removed = await DeleteAsync($"/api/v1/comments/{comment}", author);

        Assert.Equal(HttpStatusCode.NotFound, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Fact]
    public async Task Someone_who_can_comment_but_didnt_write_it_is_notAuthor_on_delete()
    {
        var place = await PlaceAsync(BoardVisibility.Private, BoardCommenting.Members);
        var callers = await CallersAsync();
        var comment = place.Content.CommentIds[callers[Caller.BoardAdmin].Id];

        using var removed = await DeleteAsync($"/api/v1/comments/{comment}", callers[Caller.BoardNormal]);

        await AssertRefusedAsync(removed, ErrorCodes.NotAuthor);
    }

    [Fact]
    public async Task An_observer_deleting_someone_elses_comment_gets_the_comment_refusal_first()
    {
        var place = await PlaceAsync(BoardVisibility.Private, BoardCommenting.Members);
        var callers = await CallersAsync();
        var comment = place.Content.CommentIds[callers[Caller.BoardAdmin].Id];

        using var removed = await DeleteAsync($"/api/v1/comments/{comment}", callers[Caller.BoardObserver]);

        await AssertRefusedAsync(removed, ErrorCodes.ReadOnlyRole);
    }

    private async Task<Place> PlaceAsync(BoardVisibility visibility, BoardCommenting commenting)
    {
        var place = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility);
        await FromDatabaseAsync(database => database.Boards
            .Where(board => board.Id == place.BoardId)
            .ExecuteUpdateAsync(board => board.SetProperty(found => found.Commenting, commenting)));
        return place;
    }

    private async Task<HttpResponseMessage> CommentAsync(Place place, Caller caller) =>
        await PostAsync(CommentPath(place.Content.CardId), new { text = "Looks good." }, (await CallersAsync())[caller]);

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(code, error.Code);
        Assert.Null(error.Field);
    }
}
