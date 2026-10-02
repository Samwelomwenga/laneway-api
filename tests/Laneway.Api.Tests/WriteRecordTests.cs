using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

public class WriteRecordTests
{
    [Fact]
    public void A_body_missing_a_required_field_names_it()
    {
        var dto = new CopyListDto(BoardId: null, Name: "Copy", Keep: null, Position: null, Before: null, After: null);

        var thrown = Assert.Throws<MissingWriteFieldException>(() => CopyListWrite.Of(dto));

        Assert.Equal("boardId", thrown.Field);
    }

    [Fact]
    public void A_label_id_that_came_through_null_names_its_place_in_the_list()
    {
        var dto = new UpdateCardDto(
            Title: "Write the brief",
            Description: null,
            DueDate: null,
            IsDueComplete: false,
            StartDate: null,
            DueReminderMinutes: null,
            LabelIds: [Guid.NewGuid(), null]);

        var thrown = Assert.Throws<MissingWriteFieldException>(() => UpdateCardWrite.Of(dto));

        Assert.Equal("labelIds[1]", thrown.Field);
    }
}

[Collection(ApiCollection.Name)]
public class StoredCopyBodyTests : ApiTests
{
    public StoredCopyBodyTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_stored_list_copy_body_with_no_board_fails_the_job()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");

        var jobId = await QueueStoredAsync(CopyJobKind.List, list.Id, """{"name":"Copy"}""");

        var failed = await AwaitCopyJobAsync(jobId, CopyJobStatus.Failed);

        Assert.Null(failed.ResultId);
        Assert.Equal(ErrorCodes.InternalError, Assert.Single(failed.Errors!).Code);
    }

    [Fact]
    public async Task A_stored_board_copy_body_with_no_workspace_fails_the_job()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        var jobId = await QueueStoredAsync(CopyJobKind.Board, board.Id, """{"name":"Copy"}""");

        var failed = await AwaitCopyJobAsync(jobId, CopyJobStatus.Failed);

        Assert.Null(failed.ResultId);
        Assert.Equal(ErrorCodes.InternalError, Assert.Single(failed.Errors!).Code);
    }

    private async Task<Guid> QueueStoredAsync(CopyJobKind kind, Guid sourceId, string request)
    {
        var id = Guid.NewGuid();
        await FromDatabaseAsync(async context =>
        {
            context.CopyJobs.Add(new CopyJob
            {
                Id = id,
                Kind = kind,
                SourceId = sourceId,
                Request = JsonDocument.Parse(request),
                Status = CopyJobStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ActorId
            });

            return await context.SaveChangesAsync();
        });

        return id;
    }
}
