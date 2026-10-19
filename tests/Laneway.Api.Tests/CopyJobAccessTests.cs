using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CopyJobAccessTests : AccessMatrix
{
    public CopyJobAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_list_copy_onto_a_board_the_actor_now_only_observes_fails_readOnlyRole_on_boardId()
    {
        var source = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var target = await SeedForeignBoardAsync(BoardVisibility.Private, BoardRole.Observer);

        var job = await QueueAsync(CopyJobKind.List, source.Content.ListId, new { boardId = target });

        Assert.Equal(["boardId readOnlyRole"], await FailedWithAsync(job));
    }

    [Fact]
    public async Task A_list_copy_onto_a_board_the_actor_can_no_longer_read_fails_like_a_missing_board()
    {
        var source = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var target = await SeedForeignBoardAsync(BoardVisibility.Private, actorRole: null);

        var job = await QueueAsync(CopyJobKind.List, source.Content.ListId, new { boardId = target });

        Assert.Equal(["boardId notFound"], await FailedWithAsync(job));
    }

    [Fact]
    public async Task A_list_copy_from_a_board_the_actor_can_no_longer_read_fails_like_a_missing_list()
    {
        var target = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var hidden = await SeedContentAsync(await SeedForeignBoardAsync(BoardVisibility.Private, actorRole: null));

        var job = await QueueAsync(CopyJobKind.List, hidden.ListId, new { boardId = target.BoardId });

        Assert.Equal([" notFound"], await FailedWithAsync(job));
    }

    [Fact]
    public async Task A_board_copy_into_a_workspace_the_actor_is_not_a_member_of_fails_requiresWorkspaceMember()
    {
        var source = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var target = await SeedPlaceAsync(WorkspaceVisibility.Public, BoardVisibility.Private);
        await FromDatabaseAsync(database => database.WorkspaceMembers
            .Where(member => member.WorkspaceId == target.WorkspaceId && member.UserId == ActorId)
            .ExecuteDeleteAsync());

        var job = await QueueAsync(CopyJobKind.Board, source.BoardId, new { workspaceId = target.WorkspaceId });

        Assert.Equal(["workspaceId requiresWorkspaceMember"], await FailedWithAsync(job));
    }

    [Fact]
    public async Task A_board_copy_from_a_board_the_actor_can_no_longer_read_fails_like_a_missing_board()
    {
        var target = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var hidden = await SeedForeignBoardAsync(BoardVisibility.Workspace, actorRole: null);

        var job = await QueueAsync(CopyJobKind.Board, hidden, new { workspaceId = target.WorkspaceId });

        Assert.Equal([" notFound"], await FailedWithAsync(job));
    }

    [Fact]
    public async Task A_deleted_actors_queued_jobs_fail_like_a_missing_source()
    {
        var place = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Public);
        await FromDatabaseAsync(database => database.Users
            .Where(user => user.Id == ActorId)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.DeletedAt, DateTime.UtcNow)));

        var list = await QueueAsync(CopyJobKind.List, place.Content.ListId, new { boardId = place.BoardId });
        var board = await QueueAsync(CopyJobKind.Board, place.BoardId, new { workspaceId = place.WorkspaceId });

        Assert.Equal([" notFound"], await StoredFailureAsync(list));
        Assert.Equal([" notFound"], await StoredFailureAsync(board));
    }

    [Fact]
    public async Task Only_the_starter_reads_a_copy_job_and_everyone_else_gets_404()
    {
        var callers = await CallersAsync();
        var place = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Public);
        var job = await CopyListAsync(place.Content.ListId, place.BoardId);
        var failures = new List<string>();

        foreach (var (caller, user) in callers)
        {
            using var response = await GetAsync(CopyJobPath(job.Id), user);
            var message = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("message").GetString();
            if (response.StatusCode != HttpStatusCode.NotFound || message != "Copy job not found")
            {
                failures.Add($"{caller}: {(int)response.StatusCode} {message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(job.Id, (await ReadCopyJobAsync(job.Id)).Id);
    }

    [Fact]
    public async Task A_board_copy_is_Private_with_default_settings_and_the_actor_as_its_only_member()
    {
        var callers = await CallersAsync();
        var source = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Public);
        await FromDatabaseAsync(database => database.Boards
            .Where(board => board.Id == source.BoardId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(board => board.Commenting, BoardCommenting.Public)
                .SetProperty(board => board.AddingMembers, BoardAddingMembers.Members)
                .SetProperty(board => board.SelfJoin, false)));
        var stranger = callers[Caller.Stranger];
        var mine = NewWorkspace(WorkspaceVisibility.Private);
        await FromDatabaseAsync(async database =>
        {
            database.WorkSpaces.Add(mine);
            AddWorkspaceMember(database, mine.Id, stranger.Id, WorkspaceRole.Normal);
            return await database.SaveChangesAsync();
        });

        using var response = await SendBoardCopyAsync(source.BoardId, new { workspaceId = mine.Id }, stranger);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = (await response.ReadEnvelope<CopyJobDto>()).Data!;
        var copyId = (await FinishedJobAsync(queued.Id)).ResultId!.Value;

        var copy = await FromDatabaseAsync(database => database.Boards.AsNoTracking().SingleAsync(board => board.Id == copyId));
        var members = await FromDatabaseAsync(database => database.BoardMembers.AsNoTracking()
            .Where(member => member.BoardId == copyId)
            .Select(member => new { member.UserId, member.Role })
            .ToListAsync());
        Assert.Equal(BoardVisibility.Private, copy.Visibility);
        Assert.Equal(BoardCommenting.Members, copy.Commenting);
        Assert.Equal(BoardAddingMembers.Admins, copy.AddingMembers);
        Assert.True(copy.SelfJoin);
        Assert.Equal([new { UserId = stranger.Id, Role = BoardRole.Admin }], members);
    }

    private async Task<Guid> SeedForeignBoardAsync(BoardVisibility visibility, BoardRole? actorRole)
    {
        var owner = await CreateUserAsync();
        var workspace = NewWorkspace(WorkspaceVisibility.Private);
        var board = new Board
        {
            Id = Guid.NewGuid(),
            Name = "Foreign board",
            WorkspaceId = workspace.Id,
            Visibility = visibility,
            CreatedBy = owner.Id
        };

        await FromDatabaseAsync(async database =>
        {
            database.WorkSpaces.Add(workspace);
            database.Boards.Add(board);
            AddWorkspaceMember(database, workspace.Id, owner.Id, WorkspaceRole.Admin);
            AddBoardMember(database, board.Id, owner.Id, BoardRole.Admin);
            if (actorRole is { } role)
            {
                AddBoardMember(database, board.Id, ActorId, role);
            }

            return await database.SaveChangesAsync();
        });

        return board.Id;
    }

    private async Task<Guid> QueueAsync(CopyJobKind kind, Guid sourceId, object request)
    {
        var id = Guid.NewGuid();
        await FromDatabaseAsync(async database =>
        {
            database.CopyJobs.Add(new CopyJob
            {
                Id = id,
                Kind = kind,
                SourceId = sourceId,
                Request = JsonSerializer.SerializeToDocument(request),
                Status = CopyJobStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ActorId
            });

            return await database.SaveChangesAsync();
        });

        await FromServicesAsync(services =>
        {
            services.GetRequiredService<CopyJobSignal>().Poke();
            return Task.FromResult(true);
        });

        return id;
    }

    private async Task<List<string>> FailedWithAsync(Guid job)
    {
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);
        return failed.Errors!.ConvertAll(error => $"{error.Field} {error.Code}");
    }

    private async Task<List<string>> StoredFailureAsync(Guid job)
    {
        var stored = await FinishedJobAsync(job);
        return CopyJobView.Of(stored).Errors?.ConvertAll(error => $"{error.Field} {error.Code}")
            ?? [stored.Status.ToString()];
    }

    private async Task<CopyJob> FinishedJobAsync(Guid job)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var stored = await FromDatabaseAsync(database => database.CopyJobs
                .AsNoTracking()
                .Where(found => found.Id == job && found.FinishedAt != null)
                .FirstOrDefaultAsync());
            if (stored is not null)
            {
                return stored;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"Copy job {job} never finished.");
    }
}
