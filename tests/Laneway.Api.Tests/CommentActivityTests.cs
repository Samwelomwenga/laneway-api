using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CommentActivityTests : ApiTests
{
    public CommentActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_comment_is_one_entry_naming_its_card_list_board_and_workspace()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        var comment = await CreateCommentAsync(card.Id, "The client wants it by Friday.");

        Assert.Equal(ActivityType.Comment, comment.Type);
        Assert.Equal("The client wants it by Friday.", comment.Text);
        Assert.Null(comment.UpdatedAt);
        ActivityAssert.Place(
            comment, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(comment, ActorUser);
        Assert.Equal(card.Id, comment.Data.Id("card.id"));
        Assert.Equal("Write the brief", comment.Data.Text("card.title"));
        Assert.Equal(list.Id, comment.Data.Id("list.id"));
        Assert.Equal("Doing", comment.Data.Text("list.name"));
        Assert.Equal(board.Id, comment.Data.Id("board.id"));
        Assert.Equal("Roadmap", comment.Data.Text("board.name"));
        Assert.Equal(workspace.Id, comment.Data.Id("workspace.id"));
        Assert.Equal("Design", comment.Data.Text("workspace.name"));
        Assert.False(comment.Data.Has("text"));
    }

    [Fact]
    public async Task A_comment_reads_back_through_the_log()
    {
        var card = await CardAsync();

        var comment = await CreateCommentAsync(card.Id, "The client wants it by Friday.");

        var page = await ActivityAsync($"cardId={card.Id}&type=comment");

        var entry = Assert.Single(page.Entries);
        Assert.Equal(comment.Id, entry.Id);
        Assert.Equal("The client wants it by Friday.", entry.Text);
    }

    [Fact]
    public async Task Commenting_writes_no_other_entry_and_never_stamps_the_card()
    {
        var card = await CardAsync();

        await CreateCommentAsync(card.Id, "The client wants it by Friday.");

        var page = await ActivityAsync($"cardId={card.Id}");
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ActivityType.Comment, page.Entries[0].Type);
        Assert.Equal(ActivityType.CreateCard, page.Entries[1].Type);

        var read = await ReadCardAsync(card.Id);
        Assert.Null(read.UpdatedAt);
        Assert.Null(read.UpdatedBy);
    }

    [Fact]
    public async Task Text_is_stored_exactly_as_sent()
    {
        var card = await CardAsync();

        var comment = await CreateCommentAsync(card.Id, "  - a list item\n  - another\n");

        Assert.Equal("  - a list item\n  - another\n", comment.Text);
    }

    [Fact]
    public async Task An_edit_replaces_the_text_and_sets_updated_at()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var response = await EditCommentAsync(card.Id, comment.Id, new { text = "By Monday." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.ReadEnvelope<ActivityEntryDto>()).Data!;
        Assert.Equal(comment.Id, edited.Id);
        Assert.Equal("By Monday.", edited.Text);
        Assert.NotNull(edited.UpdatedAt);

        var entry = Assert.Single((await ActivityAsync($"cardId={card.Id}&type=comment")).Entries);
        Assert.Equal("By Monday.", entry.Text);
    }

    [Fact]
    public async Task An_edit_leaves_the_data_the_comment_was_written_with_alone()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var renamed = await UpdateCardAsync(card.Id, "Write the final brief");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        using var response = await EditCommentAsync(card.Id, comment.Id, new { text = "By Monday." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.ReadEnvelope<ActivityEntryDto>()).Data!;
        Assert.Equal("Write the brief", edited.Data.Text("card.title"));
        Assert.Equal(card.Id, edited.CardId);
    }

    [Fact]
    public async Task Sending_the_text_a_comment_already_has_changes_nothing()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var response = await EditCommentAsync(card.Id, comment.Id, new { text = "By Friday." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.ReadEnvelope<ActivityEntryDto>()).Data!.UpdatedAt);

        var entry = Assert.Single((await ActivityAsync($"cardId={card.Id}&type=comment")).Entries);
        Assert.Null(entry.UpdatedAt);
    }

    [Fact]
    public async Task A_trailing_space_counts_as_a_change()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var response = await EditCommentAsync(card.Id, comment.Id, new { text = "By Friday. " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.ReadEnvelope<ActivityEntryDto>()).Data!;
        Assert.Equal("By Friday. ", edited.Text);
        Assert.NotNull(edited.UpdatedAt);
    }

    [Fact]
    public async Task An_edit_writes_no_other_entry_and_never_stamps_the_card()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var response = await EditCommentAsync(card.Id, comment.Id, new { text = "By Monday." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}");
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ActivityType.Comment, page.Entries[0].Type);

        var read = await ReadCardAsync(card.Id);
        Assert.Null(read.UpdatedAt);
        Assert.Null(read.UpdatedBy);
    }

    [Fact]
    public async Task A_delete_removes_the_entry_and_writes_no_other()
    {
        var card = await CardAsync();
        var comment = await CreateCommentAsync(card.Id, "By Friday.");

        using var response = await RemoveCommentAsync(card.Id, comment.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}");
        Assert.Equal(ActivityType.CreateCard, Assert.Single(page.Entries).Type);

        var read = await ReadCardAsync(card.Id);
        Assert.Null(read.UpdatedAt);
        Assert.Null(read.UpdatedBy);
    }

    [Fact]
    public async Task Two_comments_on_one_card_both_stay()
    {
        var card = await CardAsync();

        await CreateCommentAsync(card.Id, "By Friday.");
        await CreateCommentAsync(card.Id, "The client agreed.");

        var page = await ActivityAsync($"cardId={card.Id}&type=comment");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("The client agreed.", page.Entries[0].Text);
        Assert.Equal("By Friday.", page.Entries[1].Text);
    }

    [Fact]
    public async Task Text_of_exactly_ten_thousand_characters_is_stored_whole()
    {
        var card = await CardAsync();
        var text = new string('a', FieldLimits.CommentText);

        var comment = await CreateCommentAsync(card.Id, text);

        Assert.Equal(FieldLimits.CommentText, comment.Text!.Length);
    }

    private async Task<CardDto> CardAsync()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        return await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
    }
}
