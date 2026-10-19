using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class WorkspaceAccessTests : AccessMatrix
{
    public WorkspaceAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task GET_workspaces_lists_the_workspaces_a_caller_is_a_member_or_guest_of()
    {
        var callers = await CallersAsync();
        var hidden = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var open = await SeedPlaceAsync(WorkspaceVisibility.Public, BoardVisibility.Private);
        await FromDatabaseAsync(async database =>
        {
            var elsewhere = NewWorkspace(WorkspaceVisibility.Public);
            database.WorkSpaces.Add(elsewhere);
            AddWorkspaceMember(database, elsewhere.Id, ActorId, WorkspaceRole.Admin);
            return await database.SaveChangesAsync();
        });
        var both = new[] { hidden.WorkspaceId, open.WorkspaceId };
        var expected = new Dictionary<Caller, Guid[]>
        {
            [Caller.BoardAdmin] = both,
            [Caller.BoardNormal] = both,
            [Caller.BoardObserver] = both,
            [Caller.Guest] = both,
            [Caller.WorkspaceAdmin] = both,
            [Caller.WorkspaceNormal] = both,
            [Caller.Stranger] = []
        };
        var failures = new List<string>();

        foreach (var (caller, ids) in expected)
        {
            using var response = await GetAsync("/api/v1/workspaces?pageSize=50", callers[caller]);
            var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
            var seen = data.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet();
            if (!seen.SetEquals(ids))
            {
                failures.Add($"{caller}: saw {string.Join(", ", seen)}");
            }

            var limited = data.EnumerateArray().Any(item => !item.TryGetProperty("description", out _));
            if (limited != (caller == Caller.Guest))
            {
                failures.Add($"{caller}: limited rows {limited}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public Task GET_a_workspace() =>
        RunWorkspaceRowAsync(Act.ReadWorkspace, 200, (place, caller) =>
            GetAsync($"/api/v1/workspaces/{place.WorkspaceId}", caller));

    [Fact]
    public async Task POST_workspaces_is_open_to_every_signed_in_caller_who_becomes_its_admin()
    {
        var callers = await CallersAsync();
        var failures = new List<string>();

        foreach (var (caller, user) in callers)
        {
            using var response = await PostAsync(
                "/api/v1/workspaces", new { name = $"{caller}'s", visibility = "Private" }, user);
            if (response.StatusCode != HttpStatusCode.Created)
            {
                failures.Add($"{caller}: status {(int)response.StatusCode}");
                continue;
            }

            var id = (await response.ReadEnvelope<WorkSpaceDto>()).Data!.Id;
            var role = await FromDatabaseAsync(database => Task.FromResult(
                database.WorkspaceMembers.Where(member => member.WorkspaceId == id)
                    .Select(member => new { member.UserId, member.Role })
                    .ToList()));
            if (role.Count != 1 || role[0].UserId != user.Id || role[0].Role != WorkspaceRole.Admin)
            {
                failures.Add($"{caller}: members {JsonSerializer.Serialize(role)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public Task PUT_a_workspace() =>
        RunWorkspaceRowAsync(Act.AdministerWorkspace, 200, (place, caller) =>
            PutAsync($"/api/v1/workspaces/{place.WorkspaceId}",
                new { name = "Renamed", description = "Changed", visibility = "Public" }, caller));

    [Fact]
    public Task DELETE_a_workspace_lets_its_admin_through_to_the_409_for_the_guests_board() =>
        RunWorkspaceRowAsync(Act.AdministerWorkspace, StatusCodes.Status409Conflict, (place, caller) =>
            DeleteAsync($"/api/v1/workspaces/{place.WorkspaceId}", caller));

    [Fact]
    public async Task GET_a_workspaces_boards_lists_what_each_caller_may_read()
    {
        var callers = await CallersAsync();
        var failures = new List<string>();

        foreach (var visibility in Enum.GetValues<WorkspaceVisibility>())
        {
            var place = await SeedPlaceAsync(visibility, BoardVisibility.Private);
            var shared = await SeedBoardAsync(place.WorkspaceId, BoardVisibility.Workspace);
            var open = await SeedBoardAsync(place.WorkspaceId, BoardVisibility.Public);
            var elsewhere = await SeedPlaceAsync(WorkspaceVisibility.Public, BoardVisibility.Public);
            await SeedBoardAsync(elsewhere.WorkspaceId, BoardVisibility.Public, Caller.Guest, Caller.Stranger);
            var publicWorkspace = visibility == WorkspaceVisibility.Public;
            var expected = new Dictionary<Caller, Guid[]?>
            {
                [Caller.BoardAdmin] = [place.BoardId, shared, open],
                [Caller.BoardNormal] = [place.BoardId, shared, open],
                [Caller.BoardObserver] = [place.BoardId, shared, open],
                [Caller.Guest] = publicWorkspace ? [place.BoardId, open] : [place.BoardId],
                [Caller.WorkspaceAdmin] = [shared, open],
                [Caller.WorkspaceNormal] = [shared, open],
                [Caller.Stranger] = publicWorkspace ? [open] : null
            };

            foreach (var (caller, ids) in expected)
            {
                using var response = await GetAsync($"{BoardsPath(place.WorkspaceId)}?pageSize=50", callers[caller]);
                if (ids is null)
                {
                    if (response.StatusCode != HttpStatusCode.NotFound)
                    {
                        failures.Add($"{visibility} workspace, {caller}: status {(int)response.StatusCode}");
                    }

                    continue;
                }

                var seen = (await response.ReadPage<BoardDto>()).Data!.Select(board => board.Id).ToHashSet();
                if (!seen.SetEquals(ids))
                {
                    failures.Add($"{visibility} workspace, {caller}: saw {string.Join(", ", seen)}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public Task POST_a_board() =>
        RunWorkspaceRowAsync(Act.CreateBoard, 201, (place, caller) =>
            PostAsync(BoardsPath(place.WorkspaceId), new { name = "New board", visibility = "Private" }, caller));
}
