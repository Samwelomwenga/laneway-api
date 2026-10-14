using System.Net;
using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardViewTests : ApiTests
{
    public BoardViewTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task The_board_view_holds_its_open_lists_in_position_order_with_their_open_cards()
    {
        var board = await NewBoardAsync();
        var doing = await CreateListAsync(board.Id, "Doing");
        var todo = await CreateListAsync(board.Id, "To do", position: "top");
        var old = await CreateListAsync(board.Id, "Old");
        await CreateCardAsync(old.Id, "Hidden with its list");
        await SetListArchivedAsync(old.Id, true);
        var studio = await CreateCardAsync(todo.Id, "Book the studio");
        var brief = await CreateCardAsync(todo.Id, "Write the brief", position: "top");
        var invoice = await CreateCardAsync(todo.Id, "Send the invoice");
        await SetCardArchivedAsync(invoice.Id, true);

        var view = await ReadBoardViewAsync(board.Id);

        Assert.Equal(board.Id, view.Id);
        Assert.Equal("Roadmap", view.Name);
        Assert.Equal([todo.Id, doing.Id], view.Lists.Select(l => l.Id));
        Assert.Equal("To do", view.Lists[0].Name);
        Assert.Equal([brief.Id, studio.Id], view.Lists[0].Cards.Select(c => c.Id));
        Assert.Empty(view.Lists[1].Cards);
    }

    [Fact]
    public async Task The_board_view_holds_its_labels_by_name()
    {
        var board = await NewBoardAsync();
        var urgent = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        var bug = await CreateLabelAsync(board.Id, "Bug", Color.Green);
        var docs = await CreateLabelAsync(board.Id, "Docs", Color.Blue);

        var view = await ReadBoardViewAsync(board.Id);

        Assert.Equal([bug.Id, docs.Id, urgent.Id], view.Labels.Select(l => l.Id));
        Assert.Equal("Bug", view.Labels[0].Name);
    }

    [Fact]
    public async Task Board_view_cards_say_whether_they_have_a_description_and_keep_their_label_ids()
    {
        var board = await NewBoardAsync();
        var list = await CreateListAsync(board.Id, "To do");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        var brief = await CreateCardAsync(list.Id, "Write the brief", labelIds: [label.Id]);
        using (var update = await UpdateCardAsync(brief.Id, "Write the brief", "Two pages, no jargon.", labelIds: [label.Id]))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }
        await CreateCardAsync(list.Id, "Book the studio");

        using var response = await GetAsync($"/api/v1/boards/{board.Id}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cards = json.RootElement.GetProperty("data").GetProperty("lists")[0].GetProperty("cards");

        Assert.True(cards[0].GetProperty("hasDescription").GetBoolean());
        Assert.False(cards[1].GetProperty("hasDescription").GetBoolean());
        Assert.All(cards.EnumerateArray(), card => Assert.False(card.Has("description"), card.GetRawText()));
        Assert.Equal(label.Id, Assert.Single(cards[0].GetProperty("labelIds").EnumerateArray()).GetGuid());
        Assert.False(cards[0].Has("labels"), cards[0].GetRawText());
    }

    [Fact]
    public async Task An_archived_board_still_shows_its_open_lists_and_cards()
    {
        var board = await NewBoardAsync();
        var list = await CreateListAsync(board.Id, "To do");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        await SetBoardArchivedAsync(board.Id, true);

        var view = await ReadBoardViewAsync(board.Id);

        Assert.Equal(list.Id, Assert.Single(view.Lists).Id);
        Assert.Equal(card.Id, Assert.Single(view.Lists[0].Cards).Id);
    }

    [Fact]
    public async Task The_board_view_takes_no_archived_param()
    {
        var board = await NewBoardAsync();

        using var response = await GetAsync($"/api/v1/boards/{board.Id}?archived=only");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("archived", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Board_writes_return_the_plain_board()
    {
        var board = await NewBoardAsync();
        var other = await CreateWorkspaceAsync("Ops");
        await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        await CreateListAsync(board.Id, "To do");

        await AssertLacksAsync(() => PostAsync(BoardsPath(other.Id), new { name = "Sprint 15", visibility = "Workspace" }), "labels", "lists");
        await AssertLacksAsync(() => UpdateBoardAsync(board.Id, "Roadmap 2027"), "labels", "lists");
        await AssertLacksAsync(() => MoveBoardAsync(board.Id, other.Id), "labels", "lists");
    }

    private async Task<BoardDto> NewBoardAsync()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        return await CreateBoardAsync(workspace.Id, "Roadmap");
    }

    private async Task<BoardViewReply> ReadBoardViewAsync(Guid id)
    {
        using var response = await GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<BoardViewReply>()).Data!;
    }
}

public sealed record BoardViewReply(
    Guid Id,
    string Name,
    List<LabelDto> Labels,
    List<BoardListReply> Lists);

public sealed record BoardListReply(
    Guid Id,
    string Name,
    List<BoardCardReply> Cards);

public sealed record BoardCardReply(
    Guid Id,
    string Title);
