using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ListCopyActivityTests : ApiTests
{
    public ListCopyActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_copy_on_the_same_board_writes_one_entry_naming_both_lists()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        var copy = await CopiedListAsync(source.Id, board.Id, name: "Doing next");

        var entry = Assert.Single((await ActivityAsync($"listId={copy.Id}&type=copyList")).Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id, listId: copy.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(copy.Id, entry.Data.Id("list.id"));
        Assert.Equal("Doing next", entry.Data.Text("list.name"));
        Assert.Equal(source.Id, entry.Data.Id("source.id"));
        Assert.Equal("Doing", entry.Data.Text("source.name"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("from"));
        Assert.False(entry.Data.Has("createdLabels"));
    }

    [Fact]
    public async Task A_copy_stores_the_keep_it_resolved()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        var everything = await CopiedListAsync(source.Id, board.Id);
        var nothing = await CopiedListAsync(source.Id, board.Id, keep: []);
        var some = await CopiedListAsync(source.Id, board.Id, keep: ["attachments", "labels"]);

        Assert.Equal(
            [nameof(CopyPart.Labels), nameof(CopyPart.Checklists), nameof(CopyPart.Attachments)],
            await KeepAsync(everything.Id));
        Assert.Empty(await KeepAsync(nothing.Id));
        Assert.Equal([nameof(CopyPart.Labels), nameof(CopyPart.Attachments)], await KeepAsync(some.Id));
    }

    [Fact]
    public async Task A_copy_onto_another_board_records_the_board_and_the_labels_it_created()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");
        var red = await CreateLabelAsync(from.Id, "Bug", Color.Red);
        var green = await CreateLabelAsync(from.Id, null, Color.Green);
        await CreateLabelAsync(to.Id, "bug", Color.Blue);
        await CreateCardAsync(source.Id, "Write the brief", labelIds: [red.Id, green.Id]);

        var copy = await CopiedListAsync(source.Id, to.Id);

        var entry = Assert.Single((await ActivityAsync($"listId={copy.Id}&type=copyList")).Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: to.Id, listId: copy.Id, fromBoardId: from.Id);
        Assert.Equal("Roadmap", entry.Data.Text("from.board.name"));
        Assert.False(entry.Data.Has("from.workspace"));

        var created = entry.Data.At("createdLabels").EnumerateArray().ToList();
        var carried = Assert.Single(created);
        Assert.Equal("Green", carried.Text("color"));
        Assert.NotEqual(green.Id, carried.Id("id"));
    }

    [Fact]
    public async Task A_copy_into_another_workspace_records_every_level_that_differs()
    {
        var from = await CreateWorkspaceAsync("Design");
        var to = await CreateWorkspaceAsync("Sales");
        var roadmap = await CreateBoardAsync(from.Id, "Roadmap");
        var pipeline = await CreateBoardAsync(to.Id, "Pipeline");
        var source = await CreateListAsync(roadmap.Id, "Doing");

        var copy = await CopiedListAsync(source.Id, pipeline.Id);

        var entry = Assert.Single((await ActivityAsync($"listId={copy.Id}&type=copyList")).Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: to.Id,
            boardId: pipeline.Id,
            listId: copy.Id,
            fromWorkspaceId: from.Id,
            fromBoardId: roadmap.Id);
        Assert.Equal("Design", entry.Data.Text("from.workspace.name"));
        Assert.Equal("Roadmap", entry.Data.Text("from.board.name"));
    }

    [Fact]
    public async Task The_source_board_log_shows_the_copy_it_gave_away()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");

        var copy = await CopiedListAsync(source.Id, to.Id);

        var entry = Assert.Single((await ActivityAsync($"boardId={from.Id}&type=copyList")).Entries);
        Assert.Equal(copy.Id, entry.ListId);
    }

    [Fact]
    public async Task The_lists_and_cards_a_copy_creates_get_no_entries_of_their_own()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(source.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft it");

        var copy = await CopiedListAsync(source.Id, board.Id);

        var entry = Assert.Single((await ActivityAsync($"listId={copy.Id}")).Entries);
        Assert.Equal(ActivityType.CopyList, entry.Type);
        var copied = Assert.Single(await ReadCardsOfListAsync(copy.Id));
        Assert.Equal(0, (await ActivityAsync($"cardId={copied.Id}")).TotalCount);
    }

    [Fact]
    public async Task A_failed_job_writes_no_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");
        await SetBoardArchivedAsync(to.Id, true);

        var id = Guid.NewGuid();
        await FromDatabaseAsync(async context =>
        {
            context.CopyJobs.Add(new CopyJob
            {
                Id = id,
                Kind = CopyJobKind.List,
                SourceId = source.Id,
                Request = JsonSerializer.SerializeToDocument(new { boardId = to.Id }),
                Status = CopyJobStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ActorId
            });

            return await context.SaveChangesAsync();
        });
        await AwaitCopyJobAsync(id, CopyJobStatus.Failed);

        Assert.Equal(0, (await ActivityAsync("type=copyList")).TotalCount);
    }

    private async Task<List<string>> KeepAsync(Guid listId)
    {
        var entry = Assert.Single((await ActivityAsync($"listId={listId}&type=copyList")).Entries);
        return entry.Data.At("keep").EnumerateArray().Select(part => part.GetString()!).ToList();
    }

    private async Task<ListDto> CopiedListAsync(
        Guid id, Guid boardId, string? name = null, IEnumerable<string>? keep = null)
    {
        var job = await AwaitCopyJobAsync((await CopyListAsync(id, boardId, name, keep)).Id);
        return await ReadListAsync(job.ResultId!.Value);
    }
}
