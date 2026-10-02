using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardCopyActivityTests : ApiTests
{
    public BoardCopyActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_copy_in_the_same_workspace_writes_one_entry_naming_both_boards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        var copy = await CopiedBoardAsync(source.Id, workspace.Id, name: "Sprint 15");

        var entry = Assert.Single((await ActivityAsync($"boardId={copy.Id}&type=copyBoard")).Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: copy.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(copy.Id, entry.Data.Id("board.id"));
        Assert.Equal("Sprint 15", entry.Data.Text("board.name"));
        Assert.Equal(source.Id, entry.Data.Id("source.id"));
        Assert.Equal("Sprint 14", entry.Data.Text("source.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("from"));
    }

    [Fact]
    public async Task A_copy_lists_no_labels_even_when_it_copied_some()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        var bug = await CreateLabelAsync(source.Id, "Bug", Color.Red);
        await CreateCardAsync(doing.Id, "Write the brief", labelIds: [bug.Id]);

        var copy = await CopiedBoardAsync(source.Id, workspace.Id);

        Assert.Single(await ReadLabelsOfBoardAsync(copy.Id));
        var entry = Assert.Single((await ActivityAsync($"boardId={copy.Id}&type=copyBoard")).Entries);
        Assert.False(entry.Data.Has("createdLabels"));
        Assert.False(entry.Data.Has("labelSwaps"));
        Assert.False(entry.Data.Has("labels"));
    }

    [Fact]
    public async Task A_copy_stores_the_keep_it_resolved()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        var everything = await CopiedBoardAsync(source.Id, workspace.Id);
        var nothing = await CopiedBoardAsync(source.Id, workspace.Id, keep: []);
        var some = await CopiedBoardAsync(source.Id, workspace.Id, keep: ["cards", "labels"]);

        Assert.Equal(
            [
                nameof(BoardCopyPart.Labels),
                nameof(BoardCopyPart.Checklists),
                nameof(BoardCopyPart.Attachments),
                nameof(BoardCopyPart.Cards)
            ],
            await KeepAsync(everything.Id));
        Assert.Empty(await KeepAsync(nothing.Id));
        Assert.Equal([nameof(BoardCopyPart.Labels), nameof(BoardCopyPart.Cards)], await KeepAsync(some.Id));
    }

    [Fact]
    public async Task A_copy_into_another_workspace_records_where_it_came_from()
    {
        var from = await CreateWorkspaceAsync("Design");
        var to = await CreateWorkspaceAsync("Sales");
        var source = await CreateBoardAsync(from.Id, "Sprint 14");

        var copy = await CopiedBoardAsync(source.Id, to.Id);

        var entry = Assert.Single((await ActivityAsync($"boardId={copy.Id}&type=copyBoard")).Entries);
        ActivityAssert.Place(entry, workspaceId: to.Id, boardId: copy.Id, fromWorkspaceId: from.Id);
        Assert.Equal("Sales", entry.Data.Text("workspace.name"));
        Assert.Equal("Design", entry.Data.Text("from.workspace.name"));
    }

    [Fact]
    public async Task The_source_workspace_log_shows_the_copy_it_gave_away()
    {
        var from = await CreateWorkspaceAsync("Design");
        var to = await CreateWorkspaceAsync("Sales");
        var source = await CreateBoardAsync(from.Id, "Sprint 14");

        var copy = await CopiedBoardAsync(source.Id, to.Id);

        var entry = Assert.Single((await ActivityAsync($"workspaceId={from.Id}&type=copyBoard")).Entries);
        Assert.Equal(copy.Id, entry.BoardId);
    }

    [Fact]
    public async Task The_lists_and_cards_a_copy_creates_get_no_entries_of_their_own()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        var card = await CreateCardAsync(doing.Id, "Write the brief");
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(steps.Id, "Draft it");

        var copy = await CopiedBoardAsync(source.Id, workspace.Id);

        var entry = Assert.Single((await ActivityAsync($"boardId={copy.Id}")).Entries);
        Assert.Equal(ActivityType.CopyBoard, entry.Type);
        var list = Assert.Single(await ReadListsOfBoardAsync(copy.Id));
        Assert.Equal(0, (await ActivityAsync($"listId={list.Id}")).TotalCount);
        var copied = Assert.Single(await ReadCardsOfListAsync(list.Id));
        Assert.Equal(0, (await ActivityAsync($"cardId={copied.Id}")).TotalCount);
    }

    [Fact]
    public async Task A_failed_job_writes_no_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        await CreateListAsync(source.Id, "Doing");

        var id = Guid.NewGuid();
        await FromDatabaseAsync(async context =>
        {
            context.CopyJobs.Add(new CopyJob
            {
                Id = id,
                Kind = CopyJobKind.Board,
                SourceId = source.Id,
                Request = JsonSerializer.SerializeToDocument(new { workspaceId = Guid.NewGuid() }),
                Status = CopyJobStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ActorId
            });

            return await context.SaveChangesAsync();
        });
        await AwaitCopyJobAsync(id, CopyJobStatus.Failed);

        Assert.Equal(0, (await ActivityAsync("type=copyBoard")).TotalCount);
    }

    private async Task<List<string>> KeepAsync(Guid boardId)
    {
        var entry = Assert.Single((await ActivityAsync($"boardId={boardId}&type=copyBoard")).Entries);
        return entry.Data.At("keep").EnumerateArray().Select(part => part.GetString()!).ToList();
    }

    private async Task<BoardDto> CopiedBoardAsync(
        Guid id, Guid workspaceId, string? name = null, IEnumerable<string>? keep = null)
    {
        var job = await AwaitCopyJobAsync((await CopyBoardAsync(id, workspaceId, name, keep)).Id);
        return await ReadBoardAsync(job.ResultId!.Value);
    }
}
