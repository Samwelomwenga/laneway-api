using System.Net;
using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ActivityAccessTests : AccessMatrix
{
    private static readonly Caller[] Everyone = Enum.GetValues<Caller>();

    private static readonly Caller[] BoardMembers =
        [Caller.BoardAdmin, Caller.BoardNormal, Caller.BoardObserver, Caller.Guest];

    private static readonly Caller[] AllButStrangers = [.. Everyone.Except([Caller.Stranger])];

    private static readonly Caller[] WorkspaceMembers =
        [Caller.BoardAdmin, Caller.BoardNormal, Caller.BoardObserver, Caller.WorkspaceAdmin, Caller.WorkspaceNormal];

    public ActivityAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task GET_activity()
    {
        var callers = await CallersAsync();
        var seen = new Dictionary<string, (Guid[] Ids, Caller[] Readers)>();

        foreach (var (visibility, readers) in new[]
                 {
                     (BoardVisibility.Private, BoardMembers),
                     (BoardVisibility.Workspace, AllButStrangers),
                     (BoardVisibility.Public, Everyone)
                 })
        {
            var place = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility);
            seen[$"{visibility} board"] = ([.. place.Content.CommentIds.Values], readers);
            if (visibility == BoardVisibility.Private)
            {
                seen["workspace entry"] = ([await SeedEntryAsync(ActivityType.UpdateWorkspace, place.WorkspaceId)],
                    WorkspaceMembers);
            }
        }

        var hidden = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var open = await SeedPlaceAsync(WorkspaceVisibility.Public, BoardVisibility.Public);
        seen["move between a hidden and a public board"] = (
            [await SeedEntryAsync(ActivityType.MoveCard, hidden.WorkspaceId, hidden.BoardId, fromBoardId: open.BoardId)],
            Everyone);

        var deleted = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Workspace, archived: true);
        using (var response = await DeleteAsync($"/api/v1/boards/{deleted.BoardId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        seen["deleted Workspace board"] = ([.. deleted.Content.CommentIds.Values], AllButStrangers);

        var failures = new List<string>();
        foreach (var caller in Everyone)
        {
            using var response = await GetAsync("/api/v1/activity?pageSize=100", callers[caller]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var ids = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
                .GetProperty("data").EnumerateArray()
                .Select(entry => entry.GetProperty("id").GetGuid())
                .ToHashSet();

            foreach (var (name, (entryIds, readers)) in seen)
            {
                var reads = readers.Contains(caller);
                if (entryIds.Any(id => ids.Contains(id) != reads))
                {
                    failures.Add($"{caller}: {(reads ? "missed" : "saw")} the {name}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Someone_who_joins_the_workspace_after_a_board_is_deleted_reads_none_of_its_history()
    {
        var callers = await CallersAsync();
        var deleted = await DeletedBoardAsync(BoardVisibility.Workspace);
        await FromDatabaseAsync(async database =>
        {
            AddWorkspaceMember(database, deleted.WorkspaceId, callers[Caller.Stranger].Id, WorkspaceRole.Normal);
            return await database.SaveChangesAsync();
        });

        var errors = await FilterErrorsAsync($"boardId={deleted.BoardId}", callers[Caller.Stranger]);

        Assert.Equal("boardId", Assert.Single(errors).Field);
    }

    [Fact]
    public async Task A_deleted_private_board_keeps_its_members_and_not_the_workspace()
    {
        var callers = await CallersAsync();
        var deleted = await DeletedBoardAsync(BoardVisibility.Private);

        var member = await FilterAsync($"boardId={deleted.BoardId}&type=comment", callers[Caller.Guest]);
        var workspaceAdmin = await FilterErrorsAsync($"boardId={deleted.BoardId}", callers[Caller.WorkspaceAdmin]);

        Assert.Equal(deleted.Content.CommentIds.Values.ToHashSet(), member.ToHashSet());
        Assert.Equal("boardId", Assert.Single(workspaceAdmin).Field);
    }

    [Fact]
    public async Task A_deleted_workspace_keeps_its_members_for_its_own_entries()
    {
        var callers = await CallersAsync();
        var workspace = NewWorkspace(WorkspaceVisibility.Public);
        await FromDatabaseAsync(async database =>
        {
            database.WorkSpaces.Add(workspace);
            AddWorkspaceMember(database, workspace.Id, ActorId, WorkspaceRole.Admin);
            AddWorkspaceMember(database, workspace.Id, callers[Caller.WorkspaceNormal].Id, WorkspaceRole.Normal);
            return await database.SaveChangesAsync();
        });
        var update = await SeedEntryAsync(ActivityType.UpdateWorkspace, workspace.Id);
        using (var response = await DeleteAsync($"/api/v1/workspaces/{workspace.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var member = await FilterAsync($"workspaceId={workspace.Id}&type=updateWorkspace", callers[Caller.WorkspaceNormal]);
        var stranger = await FilterErrorsAsync($"workspaceId={workspace.Id}", callers[Caller.Stranger]);

        Assert.Equal(update, Assert.Single(member));
        Assert.Equal("workspaceId", Assert.Single(stranger).Field);
    }

    [Fact]
    public async Task A_filter_on_a_board_the_caller_cant_read_is_notFound_like_a_missing_one()
    {
        var callers = await CallersAsync();
        var place = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);

        var hidden = await FilterErrorsAsync($"boardId={place.BoardId}", callers[Caller.Stranger]);
        var missing = await FilterErrorsAsync($"boardId={Guid.NewGuid()}", callers[Caller.Stranger]);

        foreach (var error in new[] { Assert.Single(hidden), Assert.Single(missing) })
        {
            Assert.Equal(("boardId", ErrorCodes.NotFound), (error.Field, error.Code));
        }
    }

    [Fact]
    public async Task A_filter_on_a_hidden_card_is_valid_when_the_caller_reads_an_entry_naming_it()
    {
        var callers = await CallersAsync();
        var hidden = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var open = await SeedPlaceAsync(WorkspaceVisibility.Public, BoardVisibility.Public);
        var moved = await SeedEntryAsync(
            ActivityType.MoveCard, hidden.WorkspaceId, hidden.BoardId, hidden.Content.CardId,
            fromWorkspaceId: open.WorkspaceId, fromBoardId: open.BoardId);

        var seen = await FilterAsync($"cardId={hidden.Content.CardId}", callers[Caller.Stranger]);

        Assert.Equal(moved, Assert.Single(seen));
    }

    private async Task<Place> DeletedBoardAsync(BoardVisibility visibility)
    {
        var place = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility, archived: true);
        using var response = await DeleteAsync($"/api/v1/boards/{place.BoardId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return place;
    }

    private async Task<List<Guid>> FilterAsync(string query, TestUser caller)
    {
        using var response = await GetAsync($"/api/v1/activity?{query}", caller);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<ActivityEntryDto>()).Entries.ConvertAll(entry => entry.Id);
    }

    private async Task<List<ApiError>> FilterErrorsAsync(string query, TestUser caller)
    {
        using var response = await GetAsync($"/api/v1/activity?{query}", caller);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.ReadErrors();
    }

    private Task<Guid> SeedEntryAsync(
        ActivityType type,
        Guid workspaceId,
        Guid? boardId = null,
        Guid? cardId = null,
        Guid? fromWorkspaceId = null,
        Guid? fromBoardId = null) =>
        FromDatabaseAsync(async database =>
        {
            var entry = new ActivityEntry
            {
                Id = Guid.NewGuid(),
                Type = type,
                CreatedBy = ActorId,
                WorkspaceId = workspaceId,
                BoardId = boardId,
                CardId = cardId,
                FromWorkspaceId = fromWorkspaceId,
                FromBoardId = fromBoardId,
                Data = JsonDocument.Parse("{}")
            };
            database.ActivityEntries.Add(entry);
            await database.SaveChangesAsync();
            return entry.Id;
        });
}
