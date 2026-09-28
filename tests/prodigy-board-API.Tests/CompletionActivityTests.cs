using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class CompletionActivityTests : ApiTests
{
    private static readonly DateTime Due = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    public CompletionActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Checking_the_last_open_check_item_records_the_card_becoming_complete()
    {
        var card = await DatedCardAsync();
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var first = await CreateCheckItemAsync(checklist.Id, "Draft the outline");
        var second = await CreateCheckItemAsync(checklist.Id, "Book the room");

        await SetCheckedAsync(first.Id, true);
        await SetCheckedAsync(second.Id, true);

        var page = await ActivityAsync($"cardId={card.Id}&type=checkCheckItem");

        Assert.Equal(2, page.TotalCount);
        Assert.False(page.Entries[1].Data.Has("completion"));
        AssertCompletion(page.Entries[0], from: false, to: true);
        Assert.True((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    [Fact]
    public async Task Unchecking_a_check_item_records_the_card_no_longer_being_complete()
    {
        var card = await DatedCardAsync();
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline", isChecked: true);

        await SetCheckedAsync(checkItem.Id, false);

        var page = await ActivityAsync($"cardId={card.Id}&type=uncheckCheckItem");

        AssertCompletion(Assert.Single(page.Entries), from: true, to: false);
    }

    [Fact]
    public async Task Adding_an_unchecked_check_item_records_the_card_no_longer_being_complete()
    {
        var card = await DatedCardAsync(isDueComplete: true);
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        var page = await ActivityAsync($"cardId={card.Id}&type=createChecklist,createCheckItem");

        Assert.Equal(2, page.TotalCount);
        Assert.False(page.Entries[1].Data.Has("completion"));
        AssertCompletion(page.Entries[0], from: true, to: false);
        Assert.False((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    [Fact]
    public async Task Deleting_the_last_open_check_item_records_the_card_becoming_complete()
    {
        var card = await DatedCardAsync();
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft the outline", isChecked: true);
        var open = await CreateCheckItemAsync(checklist.Id, "Book the room");

        using var response = await DeleteAsync($"/api/v1/check-items/{open.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=deleteCheckItem");

        AssertCompletion(Assert.Single(page.Entries), from: false, to: true);
        Assert.True((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    [Fact]
    public async Task Archiving_a_checklist_with_open_items_records_the_card_becoming_complete()
    {
        var card = await DatedCardAsync();
        var done = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(done.Id, "Draft the outline", isChecked: true);
        var open = await CreateChecklistAsync(card.Id, "Stages");
        await CreateCheckItemAsync(open.Id, "Book the room");

        await SetChecklistArchivedAsync(open.Id, true);

        var page = await ActivityAsync($"cardId={card.Id}&type=archiveChecklist");

        AssertCompletion(Assert.Single(page.Entries), from: false, to: true);
        Assert.True((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    [Fact]
    public async Task Restoring_a_checklist_with_open_items_records_the_card_no_longer_being_complete()
    {
        var card = await DatedCardAsync();
        var done = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(done.Id, "Draft the outline", isChecked: true);
        var open = await CreateChecklistAsync(card.Id, "Stages");
        await CreateCheckItemAsync(open.Id, "Book the room");
        await SetChecklistArchivedAsync(open.Id, true);

        await SetChecklistArchivedAsync(open.Id, false);

        var page = await ActivityAsync($"cardId={card.Id}&type=restoreChecklist");

        AssertCompletion(Assert.Single(page.Entries), from: true, to: false);
        Assert.False((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    [Fact]
    public async Task Archiving_a_checklist_that_holds_every_check_item_keeps_the_card_complete()
    {
        var card = await DatedCardAsync();
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft the outline", isChecked: true);

        await SetChecklistArchivedAsync(checklist.Id, true);

        var page = await ActivityAsync($"cardId={card.Id}&type=archiveChecklist");

        Assert.False(Assert.Single(page.Entries).Data.Has("completion"));
        Assert.True((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    [Fact]
    public async Task A_card_put_that_adds_a_due_date_records_the_card_becoming_complete()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft the outline", isChecked: true);

        using var response = await UpdateCardAsync(card.Id, "Write the brief", dueDate: Due);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCard");

        AssertCompletion(Assert.Single(page.Entries), from: false, to: true);
        Assert.True((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    [Fact]
    public async Task A_card_put_that_leaves_the_due_date_alone_writes_no_completion()
    {
        var card = await DatedCardAsync();
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft the outline", isChecked: true);

        using var response = await UpdateCardAsync(card.Id, "Write the outline", dueDate: Due);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCard");

        Assert.False(Assert.Single(page.Entries).Data.Has("completion"));
    }

    [Fact]
    public async Task Two_checks_of_a_cards_last_open_items_at_once_record_one_completion()
    {
        var card = await DatedCardAsync();
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var first = await CreateCheckItemAsync(checklist.Id, "Draft the outline");
        var second = await CreateCheckItemAsync(checklist.Id, "Book the room");

        var responses = await Task.WhenAll(
            SendCheckedAsync(first.Id, true),
            SendCheckedAsync(second.Id, true));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            response.Dispose();
        }

        var page = await ActivityAsync($"cardId={card.Id}&type=checkCheckItem");

        Assert.Equal(2, page.TotalCount);
        AssertCompletion(
            Assert.Single(page.Entries.FindAll(entry => entry.Data.Has("completion"))), from: false, to: true);
        Assert.True((await ReadCardAsync(card.Id)).IsDueComplete);
    }

    private static void AssertCompletion(ActivityEntryDto entry, bool from, bool to)
    {
        Assert.Equal(from, entry.Data.At("completion.old").GetBoolean());
        Assert.Equal(to, entry.Data.At("completion.new").GetBoolean());
    }

    private async Task<CardDto> DatedCardAsync(bool isDueComplete = false)
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        return await CreateCardAsync(list.Id, "Write the brief", dueDate: Due, isDueComplete: isDueComplete);
    }
}
