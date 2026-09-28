using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class CommentTests : ApiTests
{
    public CommentTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_get_on_either_comment_route_is_405()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var list = await GetAsync(CommentPath(card.Id));
        using var one = await GetAsync($"{CommentPath(card.Id)}/{comment.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, list.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, one.StatusCode);
    }

    [Fact]
    public async Task Text_is_required()
    {
        var card = await CardAsync();

        foreach (var body in new object[] { new { }, new { text = (string?)null }, new { text = "   \n " } })
        {
            using var response = await SendCommentAsync(card.Id, body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = Assert.Single(await response.ReadErrors());
            Assert.Equal("text", error.Field);
            Assert.Equal(ErrorCodes.Required, error.Code);
        }
    }

    [Fact]
    public async Task Text_over_ten_thousand_is_tooLong()
    {
        var card = await CardAsync();

        using var response = await SendCommentAsync(
            card.Id, new { text = new string('a', FieldLimits.CommentText + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("text", error.Field);
        Assert.Equal(ErrorCodes.TooLong, error.Code);
    }

    [Fact]
    public async Task An_edit_checks_its_text_the_same_way()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var blank = await EditCommentAsync(card.Id, comment.Id, new { text = " " });
        using var tooLong = await EditCommentAsync(
            card.Id, comment.Id, new { text = new string('a', FieldLimits.CommentText + 1) });

        Assert.Equal(ErrorCodes.Required, Assert.Single(await blank.ReadErrors()).Code);
        Assert.Equal(ErrorCodes.TooLong, Assert.Single(await tooLong.ReadErrors()).Code);
    }

    [Fact]
    public async Task Any_other_body_field_is_unknownField()
    {
        var card = await CardAsync();

        using var response = await SendCommentAsync(card.Id, new { text = "By Friday.", cardId = card.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("cardId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task An_edit_refuses_an_unknown_body_field_too()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var response = await EditCommentAsync(
            card.Id, comment.Id, new { text = "By Monday.", updatedAt = DateTime.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("updatedAt", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Every_write_needs_the_actor_header()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");
        var path = $"{CommentPath(card.Id)}/{comment.Id}";

        var sends = new[]
        {
            SendWithoutActorAsync(HttpMethod.Post, CommentPath(card.Id), new { text = "By Monday." }),
            SendWithoutActorAsync(HttpMethod.Put, path, new { text = "By Monday." }),
            SendWithoutActorAsync(HttpMethod.Delete, path, null)
        };

        foreach (var send in sends)
        {
            using var response = await send;
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = Assert.Single(await response.ReadErrors());
            Assert.Equal(ActorFilter.HeaderName, error.Field);
            Assert.Equal(ErrorCodes.Required, error.Code);
        }
    }

    [Fact]
    public async Task The_header_is_checked_before_the_body()
    {
        var card = await CardAsync();

        using var response = await SendWithoutActorAsync(
            HttpMethod.Post, CommentPath(card.Id), new { text = "   " });

        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(ActorFilter.HeaderName, error.Field);
    }

    [Fact]
    public async Task The_body_is_checked_before_the_card()
    {
        using var response = await SendCommentAsync(Guid.NewGuid(), new { text = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("text", error.Field);
        Assert.Equal(ErrorCodes.Required, error.Code);
    }

    [Fact]
    public async Task An_unknown_card_is_404_on_every_write()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");
        var unknown = Guid.NewGuid();

        using var created = await SendCommentAsync(unknown, new { text = "By Monday." });
        using var edited = await EditCommentAsync(unknown, comment.Id, new { text = "By Monday." });
        using var removed = await RemoveCommentAsync(unknown, comment.Id);

        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Fact]
    public async Task An_unknown_entry_is_404()
    {
        var card = await CardAsync();

        using var edited = await EditCommentAsync(card.Id, Guid.NewGuid(), new { text = "By Monday." });
        using var removed = await RemoveCommentAsync(card.Id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Fact]
    public async Task An_entry_that_isnt_a_comment_is_404()
    {
        var card = await CardAsync();
        var created = Assert.Single((await ActivityAsync($"cardId={card.Id}&type=createCard")).Entries);

        using var edited = await EditCommentAsync(card.Id, created.Id, new { text = "By Monday." });
        using var removed = await RemoveCommentAsync(card.Id, created.Id);

        Assert.Equal(HttpStatusCode.NotFound, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Fact]
    public async Task A_comment_on_another_card_is_404()
    {
        var list = await CreateListAsync(
            (await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap")).Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var other = await CreateCardAsync(list.Id, "Book the studio");
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var edited = await EditCommentAsync(other.Id, comment.Id, new { text = "By Monday." });
        using var removed = await RemoveCommentAsync(other.Id, comment.Id);

        Assert.Equal(HttpStatusCode.NotFound, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Fact]
    public async Task Only_the_author_edits_or_deletes_a_comment()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");
        var ben = await CreateUserAsync();

        using var edited = await EditCommentAsync(card.Id, comment.Id, new { text = "By Monday." }, ben.Id);
        using var removed = await RemoveCommentAsync(card.Id, comment.Id, ben.Id);

        foreach (var response in new[] { edited, removed })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var error = Assert.Single(await response.ReadErrors());
            Assert.Null(error.Field);
            Assert.Equal(ErrorCodes.NotAuthor, error.Code);
        }

        var entry = Assert.Single((await ActivityAsync($"cardId={card.Id}&type=comment")).Entries);
        Assert.Equal("By Friday.", entry.Text);
    }

    [Fact]
    public async Task Anyone_can_comment_on_a_card_someone_else_commented_on()
    {
        var card = await CardAsync();
        await CreateCommentAsync(card.Id, "By Friday.");
        var ben = await CreateUserAsync();

        var comment = await CreateCommentAsync(card.Id, "I'll take it.", ben.Id);

        Assert.Equal(ben.Id, comment.CreatedBy);
        Assert.Equal(2, (await ActivityAsync($"cardId={card.Id}&type=comment")).TotalCount);
    }

    [Fact]
    public async Task Commenting_on_an_archived_card_is_409_archived()
    {
        var card = await CardAsync();
        await SetCardArchivedAsync(card.Id, true);

        using var response = await SendCommentAsync(card.Id, new { text = "By Friday." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
    }

    [Fact]
    public async Task Commenting_on_a_card_in_an_archived_list_or_board_is_409_archived()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        await SetListArchivedAsync(list.Id, true);
        using var onList = await SendCommentAsync(card.Id, new { text = "By Friday." });
        await SetListArchivedAsync(list.Id, false);

        await SetBoardArchivedAsync(board.Id, true);
        using var onBoard = await SendCommentAsync(card.Id, new { text = "By Friday." });

        Assert.Equal(HttpStatusCode.Conflict, onList.StatusCode);
        Assert.Equal(ErrorCodes.Archived, Assert.Single(await onList.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Conflict, onBoard.StatusCode);
        Assert.Equal(ErrorCodes.Archived, Assert.Single(await onBoard.ReadErrors()).Code);
    }

    [Fact]
    public async Task Editing_or_deleting_a_comment_on_an_archived_card_is_409_archived()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");
        await SetCardArchivedAsync(card.Id, true);

        using var edited = await EditCommentAsync(card.Id, comment.Id, new { text = "By Monday." });
        using var removed = await RemoveCommentAsync(card.Id, comment.Id);

        foreach (var response in new[] { edited, removed })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ErrorCodes.Archived, Assert.Single(await response.ReadErrors()).Code);
        }
    }

    [Fact]
    public async Task An_unknown_entry_is_404_before_notAuthor()
    {
        var card = await CardAsync();
        var ben = await CreateUserAsync();

        using var response = await EditCommentAsync(
            card.Id, Guid.NewGuid(), new { text = "By Monday." }, ben.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task notAuthor_comes_before_archived()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");
        await SetCardArchivedAsync(card.Id, true);
        var ben = await CreateUserAsync();

        using var edited = await EditCommentAsync(card.Id, comment.Id, new { text = "By Monday." }, ben.Id);
        using var removed = await RemoveCommentAsync(card.Id, comment.Id, ben.Id);

        Assert.Equal(HttpStatusCode.Forbidden, edited.StatusCode);
        Assert.Equal(ErrorCodes.NotAuthor, Assert.Single(await edited.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Forbidden, removed.StatusCode);
        Assert.Equal(ErrorCodes.NotAuthor, Assert.Single(await removed.ReadErrors()).Code);
    }

    [Fact]
    public async Task An_edit_checks_its_body_before_the_comment()
    {
        var card = await CardAsync();

        using var response = await EditCommentAsync(card.Id, Guid.NewGuid(), new { text = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.Required, Assert.Single(await response.ReadErrors()).Code);
    }

    [Fact]
    public async Task A_card_counts_its_comments_on_read()
    {
        var list = await CreateListAsync(
            (await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap")).Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var quiet = await CreateCardAsync(list.Id, "Book the studio");

        Assert.Equal(0, card.CommentCount);

        await CreateCommentAsync(card.Id, "By Friday.");
        await CreateCommentAsync(card.Id, "The client agreed.");

        Assert.Equal(2, (await ReadCardAsync(card.Id)).CommentCount);
        Assert.Equal(0, (await ReadCardAsync(quiet.Id)).CommentCount);

        var page = await ReadCardsAsync($"listId={list.Id}");
        Assert.Equal(2, page.Entries.Find(found => found.Id == card.Id)!.CommentCount);
        Assert.Equal(0, page.Entries.Find(found => found.Id == quiet.Id)!.CommentCount);
    }

    [Fact]
    public async Task Deleting_a_comment_drops_the_count()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var response = await RemoveCommentAsync(card.Id, comment.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(0, (await ReadCardAsync(card.Id)).CommentCount);
    }

    [Fact]
    public async Task The_comments_of_a_deleted_card_stay_in_the_log_and_never_change()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        await DeleteCardAsync(card.Id);

        using var created = await SendCommentAsync(card.Id, new { text = "One more." });
        using var edited = await EditCommentAsync(card.Id, comment.Id, new { text = "By Monday." });
        using var removed = await RemoveCommentAsync(card.Id, comment.Id);

        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);

        var entry = Assert.Single((await ActivityAsync($"cardId={card.Id}&type=comment")).Entries);
        Assert.Equal(comment.Id, entry.Id);
        Assert.Equal("By Friday.", entry.Text);
        Assert.Null(entry.UpdatedAt);
    }

    private async Task<CardDto> CardAsync()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        return await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
    }
}
