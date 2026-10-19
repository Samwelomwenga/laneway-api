using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

public sealed record Place(Guid WorkspaceId, Guid BoardId);

public abstract class AccessMatrix : ApiTests
{
    private Dictionary<Caller, TestUser>? _callers;

    protected AccessMatrix(TestStack stack) : base(stack)
    {
    }

    protected async Task<IReadOnlyDictionary<Caller, TestUser>> CallersAsync()
    {
        if (_callers is null)
        {
            _callers = [];
            foreach (var caller in Enum.GetValues<Caller>())
            {
                _callers[caller] = await CreateUserAsync();
            }
        }

        return _callers;
    }

    protected async Task<Place> SeedPlaceAsync(
        WorkspaceVisibility workspaceVisibility, BoardVisibility boardVisibility, bool archived = false)
    {
        var callers = await CallersAsync();
        var workspace = NewWorkspace(workspaceVisibility);
        var board = NewBoard(workspace.Id, boardVisibility, archived);

        await FromDatabaseAsync(async database =>
        {
            database.WorkSpaces.Add(workspace);
            database.Boards.Add(board);
            AddWorkspaceMember(database, workspace.Id, ActorId, WorkspaceRole.Admin);
            AddBoardMember(database, board.Id, ActorId, BoardRole.Admin);

            AddWorkspaceMember(database, workspace.Id, callers[Caller.BoardAdmin].Id, WorkspaceRole.Normal);
            AddWorkspaceMember(database, workspace.Id, callers[Caller.BoardNormal].Id, WorkspaceRole.Normal);
            AddWorkspaceMember(database, workspace.Id, callers[Caller.BoardObserver].Id, WorkspaceRole.Normal);
            AddWorkspaceMember(database, workspace.Id, callers[Caller.WorkspaceAdmin].Id, WorkspaceRole.Admin);
            AddWorkspaceMember(database, workspace.Id, callers[Caller.WorkspaceNormal].Id, WorkspaceRole.Normal);

            AddBoardMember(database, board.Id, callers[Caller.BoardAdmin].Id, BoardRole.Admin);
            AddBoardMember(database, board.Id, callers[Caller.BoardNormal].Id, BoardRole.Normal);
            AddBoardMember(database, board.Id, callers[Caller.BoardObserver].Id, BoardRole.Observer);
            AddBoardMember(database, board.Id, callers[Caller.Guest].Id, BoardRole.Normal);
            return await database.SaveChangesAsync();
        });

        return new Place(workspace.Id, board.Id);
    }

    protected async Task<Guid> SeedBoardAsync(Guid workspaceId, BoardVisibility visibility, params Caller[] members)
    {
        var callers = await CallersAsync();
        var board = NewBoard(workspaceId, visibility, archived: false);

        await FromDatabaseAsync(async database =>
        {
            database.Boards.Add(board);
            AddBoardMember(database, board.Id, ActorId, BoardRole.Admin);
            foreach (var member in members)
            {
                AddBoardMember(database, board.Id, callers[member].Id, BoardRole.Normal);
            }

            return await database.SaveChangesAsync();
        });

        return board.Id;
    }

    protected async Task RunBoardRowAsync(
        Act act, int success, Func<Place, TestUser, Task<HttpResponseMessage>> send, bool archived = false)
    {
        var callers = await CallersAsync();
        var failures = new List<string>();

        foreach (var visibility in Enum.GetValues<BoardVisibility>())
        {
            foreach (var caller in Enum.GetValues<Caller>())
            {
                var place = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility, archived);
                var expect = AccessExpectations.For(act, caller, visibility, success);
                await CheckCellAsync(failures, $"{visibility} board, {caller}", expect, "Board",
                    place, () => send(place, callers[caller]));
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    protected async Task RunWorkspaceRowAsync(
        Act act, int success, Func<Place, TestUser, Task<HttpResponseMessage>> send)
    {
        var callers = await CallersAsync();
        var failures = new List<string>();

        foreach (var visibility in Enum.GetValues<WorkspaceVisibility>())
        {
            foreach (var caller in Enum.GetValues<Caller>())
            {
                var place = await SeedPlaceAsync(visibility, BoardVisibility.Private);
                var expect = AccessExpectations.For(act, caller, visibility, success);
                await CheckCellAsync(failures, $"{visibility} workspace, {caller}", expect, "Workspace",
                    place, () => send(place, callers[caller]));
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private async Task CheckCellAsync(
        List<string> failures,
        string cell,
        Expect expect,
        string routeObject,
        Place place,
        Func<Task<HttpResponseMessage>> send)
    {
        var before = await SnapshotAsync(place);
        using var response = await send();
        var body = await response.Content.ReadAsStringAsync();
        var problems = new List<string>();

        if ((int)response.StatusCode != expect.Status)
        {
            problems.Add($"status {(int)response.StatusCode}");
        }
        else if (expect.Status == 404)
        {
            var message = JsonDocument.Parse(body).RootElement.GetProperty("message").GetString();
            if (message != $"{routeObject} not found")
            {
                problems.Add($"message \"{message}\"");
            }
        }
        else if (expect.Code is { } code)
        {
            var errors = JsonDocument.Parse(body).RootElement.GetProperty("errors");
            if (errors.GetArrayLength() != 1 || errors[0].GetProperty("code").GetString() != code
                || errors[0].GetProperty("field").ValueKind != JsonValueKind.Null)
            {
                problems.Add($"errors {errors.GetRawText()}");
            }
        }
        else if (expect.Limited)
        {
            var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
            var names = data.EnumerateObject().Select(property => property.Name).ToList();
            if (!names.SequenceEqual(["id", "name"]))
            {
                problems.Add($"data {data.GetRawText()}");
            }
        }

        if (expect.Status >= 400 && await SnapshotAsync(place) != before)
        {
            problems.Add("the target changed");
        }

        if (problems.Count > 0)
        {
            failures.Add($"{cell}: expected {expect}, got {string.Join(", ", problems)}. {body}");
        }
    }

    private Task<string> SnapshotAsync(Place place) =>
        FromDatabaseAsync(async database => JsonSerializer.Serialize(new
        {
            Workspace = await database.WorkSpaces.AsNoTracking()
                .Where(workspace => workspace.Id == place.WorkspaceId)
                .Select(workspace => new { workspace.Name, workspace.Description, workspace.Visibility, workspace.UpdatedAt })
                .SingleOrDefaultAsync(),
            Boards = await database.Boards.AsNoTracking()
                .Where(board => board.WorkspaceId == place.WorkspaceId)
                .OrderBy(board => board.Id)
                .Select(board => new { board.Id, board.Name, board.Description, board.Visibility, board.IsArchived, board.UpdatedAt })
                .ToListAsync()
        }));

    protected WorkSpace NewWorkspace(WorkspaceVisibility visibility) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Matrix workspace",
        Description = "Seeded for the access matrix",
        Visibility = visibility,
        CreatedBy = ActorId
    };

    private Board NewBoard(Guid workspaceId, BoardVisibility visibility, bool archived) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{visibility} board",
        WorkspaceId = workspaceId,
        Visibility = visibility,
        IsArchived = archived,
        CreatedBy = ActorId
    };

    protected static void AddWorkspaceMember(
        ApplicationDbContext database, Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
            CreatedBy = userId
        });
    }

    protected static void AddBoardMember(ApplicationDbContext database, Guid boardId, Guid userId, BoardRole role)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.BoardMembers.Add(new BoardMember
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            UserId = userId,
            Role = role,
            CreatedBy = userId
        });
    }
}
