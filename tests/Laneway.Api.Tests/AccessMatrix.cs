using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

public sealed record Place(Guid WorkspaceId, Guid BoardId, Content Content);

public sealed record Content(
    Guid ListId,
    Guid CardId,
    Guid LabelId,
    Guid CardLabelId,
    Guid ChecklistId,
    Guid CheckItemId,
    Guid AttachmentId,
    Guid FileAttachmentId,
    Guid ArchivedListId,
    Guid ArchivedCardId,
    Guid ArchivedChecklistId,
    IReadOnlyDictionary<Guid, Guid> CommentIds);

public abstract class AccessMatrix : ApiTests
{
    private static readonly byte[] MatrixFile = "A line of notes.\n"u8.ToArray();

    private static readonly string MatrixFileKey = $"matrix/{Guid.NewGuid()}";

    private Dictionary<Caller, TestUser>? _callers;

    private bool _fileStored;

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
        var content = NewContent(callers);

        await FromDatabaseAsync(async database =>
        {
            database.WorkSpaces.Add(workspace);
            database.Boards.Add(board);
            AddContent(database, workspace.Id, board.Id, content);
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

        return new Place(workspace.Id, board.Id, content);
    }

    protected async Task<Content> SeedContentAsync(Guid boardId)
    {
        var callers = await CallersAsync();
        var content = NewContent(callers);
        await FromDatabaseAsync(async database =>
        {
            var workspaceId = await database.Boards
                .Where(board => board.Id == boardId)
                .Select(board => board.WorkspaceId)
                .SingleAsync();
            AddContent(database, workspaceId, boardId, content);
            return await database.SaveChangesAsync();
        });

        return content;
    }

    private static Content NewContent(IReadOnlyDictionary<Caller, TestUser> callers) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        callers.Values.ToDictionary(caller => caller.Id, _ => Guid.NewGuid()));

    private void AddContent(ApplicationDbContext database, Guid workspaceId, Guid boardId, Content content)
    {
        var cardLabel = new Label { Id = content.CardLabelId, Name = "On the card", BoardId = boardId, CreatedBy = ActorId };
        database.Labels.Add(new Label { Id = content.LabelId, Name = "Free", BoardId = boardId, CreatedBy = ActorId });
        database.Labels.Add(cardLabel);
        database.Lists.Add(new List
        {
            Id = content.ListId, Name = "Matrix list", BoardId = boardId, Position = Placements.Gap, CreatedBy = ActorId
        });
        database.Lists.Add(new List
        {
            Id = content.ArchivedListId,
            Name = "Archived list",
            BoardId = boardId,
            Position = 2 * Placements.Gap,
            IsArchived = true,
            CreatedBy = ActorId
        });
        var card = new Card
        {
            Id = content.CardId, Title = "Matrix card", ListId = content.ListId, Position = Placements.Gap, CreatedBy = ActorId
        };
        card.Labels.Add(cardLabel);
        database.Cards.Add(card);
        database.Cards.Add(new Card
        {
            Id = content.ArchivedCardId,
            Title = "Archived card",
            ListId = content.ListId,
            Position = 2 * Placements.Gap,
            IsArchived = true,
            CreatedBy = ActorId
        });
        database.Checklists.Add(new Checklist
        {
            Id = content.ChecklistId,
            Name = "Matrix checklist",
            CardId = content.CardId,
            Position = Placements.Gap,
            CreatedBy = ActorId
        });
        database.Checklists.Add(new Checklist
        {
            Id = content.ArchivedChecklistId,
            Name = "Archived checklist",
            CardId = content.CardId,
            Position = 2 * Placements.Gap,
            IsArchived = true,
            CreatedBy = ActorId
        });
        database.CheckItems.Add(new CheckItem
        {
            Id = content.CheckItemId,
            Name = "Matrix item",
            ChecklistId = content.ChecklistId,
            Position = Placements.Gap,
            CreatedBy = ActorId
        });
        database.Attachments.Add(new Attachment
        {
            Id = content.AttachmentId,
            CardId = content.CardId,
            Kind = AttachmentKind.Link,
            Name = "Matrix link",
            Url = "https://example.com/matrix",
            CreatedBy = ActorId
        });
        database.Attachments.Add(new Attachment
        {
            Id = content.FileAttachmentId,
            CardId = content.CardId,
            Kind = AttachmentKind.File,
            Name = "Matrix file",
            FileName = "notes.txt",
            MimeType = "text/plain",
            Bytes = MatrixFile.Length,
            ObjectKey = MatrixFileKey,
            CreatedBy = ActorId
        });
        foreach (var (authorId, commentId) in content.CommentIds)
        {
            database.ActivityEntries.Add(new ActivityEntry
            {
                Id = commentId,
                Type = ActivityType.Comment,
                CreatedBy = authorId,
                WorkspaceId = workspaceId,
                BoardId = boardId,
                ListId = content.ListId,
                CardId = content.CardId,
                Data = JsonDocument.Parse("{}"),
                Text = "A matrix comment"
            });
        }
    }

    protected async Task StoreMatrixFileAsync()
    {
        if (_fileStored)
        {
            return;
        }

        await FromServicesAsync(async services =>
        {
            await services.GetRequiredService<AttachmentStorage>()
                .UploadAsync(MatrixFileKey, MatrixFile, "text/plain", CancellationToken.None);
            return true;
        });
        _fileStored = true;
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
        Act act,
        int success,
        Func<Place, TestUser, Task<HttpResponseMessage>> send,
        bool archived = false,
        string routeObject = "Board")
    {
        var callers = await CallersAsync();
        var failures = new List<string>();

        foreach (var visibility in Enum.GetValues<BoardVisibility>())
        {
            foreach (var caller in Enum.GetValues<Caller>())
            {
                var place = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility, archived);
                var expect = AccessExpectations.For(act, caller, visibility, success);
                await CheckCellAsync(failures, $"{visibility} board, {caller}", expect, routeObject,
                    place, () => send(place, callers[caller]));
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    protected async Task RunMoveRowAsync(
        Act source,
        string slot,
        int success,
        Func<Place, Place, TestUser, Task<HttpResponseMessage>> send,
        string routeObject)
    {
        var callers = await CallersAsync();
        var failures = new List<string>();
        var target = slot == "workspaceId" ? Act.CreateBoard : Act.EditBoard;

        foreach (var visibility in Enum.GetValues<BoardVisibility>())
        {
            foreach (var caller in Enum.GetValues<Caller>())
            {
                var from = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility);
                var to = await SeedOwnPlaceAsync(callers[caller]);
                var expect = AccessExpectations.For(source, caller, visibility, success);
                await CheckCellAsync(failures, $"source on a {visibility} board, {caller}", expect, routeObject,
                    from, () => send(from, to, callers[caller]));
            }
        }

        foreach (var caller in Enum.GetValues<Caller>())
        {
            if (target == Act.CreateBoard)
            {
                foreach (var visibility in Enum.GetValues<WorkspaceVisibility>())
                {
                    var to = await SeedPlaceAsync(visibility, BoardVisibility.Private);
                    var expect = AccessExpectations.For(target, caller, visibility, success).OnField(slot);
                    await CheckTargetCellAsync(failures, $"{slot} in a {visibility} workspace, {caller}", expect, to);
                }
            }
            else
            {
                foreach (var visibility in Enum.GetValues<BoardVisibility>())
                {
                    var to = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility);
                    var expect = AccessExpectations.For(target, caller, visibility, success).OnField(slot);
                    await CheckTargetCellAsync(failures, $"{slot} on a {visibility} board, {caller}", expect, to);
                }
            }

            async Task CheckTargetCellAsync(List<string> failed, string cell, Expect expect, Place to)
            {
                var from = await SeedOwnPlaceAsync(callers[caller]);
                await CheckCellAsync(
                    failed, cell, expect, routeObject, to, () => send(from, to, callers[caller]), source: from);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private async Task<Place> SeedOwnPlaceAsync(TestUser owner)
    {
        var workspace = NewWorkspace(WorkspaceVisibility.Private);
        var board = NewBoard(workspace.Id, BoardVisibility.Private, archived: false);
        var content = NewContent(await CallersAsync());

        await FromDatabaseAsync(async database =>
        {
            database.WorkSpaces.Add(workspace);
            database.Boards.Add(board);
            AddContent(database, workspace.Id, board.Id, content);
            AddWorkspaceMember(database, workspace.Id, owner.Id, WorkspaceRole.Admin);
            AddBoardMember(database, board.Id, owner.Id, BoardRole.Admin);
            return await database.SaveChangesAsync();
        });

        return new Place(workspace.Id, board.Id, content);
    }

    protected async Task RunBoardListRowAsync(
        string routeObject, Func<Place, string> path, Func<Content, Guid[]> expected)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(expected);

        var callers = await CallersAsync();
        var failures = new List<string>();
        var elsewhere = await SeedPlaceAsync(WorkspaceVisibility.Public, BoardVisibility.Public);
        var foreign = expected(elsewhere.Content).ToHashSet();

        foreach (var visibility in Enum.GetValues<BoardVisibility>())
        {
            var place = await SeedPlaceAsync(WorkspaceVisibility.Private, visibility);
            foreach (var caller in Enum.GetValues<Caller>())
            {
                var cell = $"{visibility} board, {caller}";
                var expect = AccessExpectations.For(Act.ReadBoard, caller, visibility, 200);
                using var response = await GetAsync(path(place), callers[caller]);
                var body = await response.Content.ReadAsStringAsync();
                if ((int)response.StatusCode != expect.Status)
                {
                    failures.Add($"{cell}: expected {expect}, got status {(int)response.StatusCode}. {body}");
                    continue;
                }

                var root = JsonDocument.Parse(body).RootElement;
                if (expect.Status == 404)
                {
                    var message = root.GetProperty("message").GetString();
                    if (message != $"{routeObject} not found")
                    {
                        failures.Add($"{cell}: message \"{message}\"");
                    }

                    continue;
                }

                var seen = root.GetProperty("data").EnumerateArray()
                    .Select(item => item.GetProperty("id").GetGuid())
                    .ToHashSet();
                if (!seen.SetEquals(expected(place.Content)) || seen.Overlaps(foreign))
                {
                    failures.Add($"{cell}: saw {string.Join(", ", seen)}");
                }
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
        Func<Task<HttpResponseMessage>> send,
        Place? source = null)
    {
        var before = await SnapshotAsync(place, source);
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
                || errors[0].GetProperty("field").GetString() != expect.Field)
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

        if (expect.Status >= 400 && await SnapshotAsync(place, source) != before)
        {
            problems.Add("the target changed");
        }

        if (problems.Count > 0)
        {
            failures.Add($"{cell}: expected {expect}, got {string.Join(", ", problems)}. {body}");
        }
    }

    private async Task<string> SnapshotAsync(Place place, Place? source) =>
        source is null ? await SnapshotAsync(place) : await SnapshotAsync(place) + await SnapshotAsync(source);

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
                .Select(board => new
                {
                    board.Id,
                    board.Name,
                    board.Description,
                    board.Visibility,
                    board.IsArchived,
                    board.Commenting,
                    board.AddingMembers,
                    board.SelfJoin,
                    board.UpdatedAt
                })
                .ToListAsync(),
            WorkspaceMembers = await database.WorkspaceMembers.AsNoTracking()
                .Where(member => member.WorkspaceId == place.WorkspaceId)
                .OrderBy(member => member.UserId)
                .Select(member => new { member.UserId, member.Role })
                .ToListAsync(),
            BoardMembers = await database.BoardMembers.AsNoTracking()
                .Where(member => database.Boards.Any(board => board.Id == member.BoardId && board.WorkspaceId == place.WorkspaceId))
                .OrderBy(member => member.BoardId).ThenBy(member => member.UserId)
                .Select(member => new { member.BoardId, member.UserId, member.Role })
                .ToListAsync(),
            Lists = await database.Lists.AsNoTracking()
                .Where(list => database.Boards.Any(board => board.Id == list.BoardId && board.WorkspaceId == place.WorkspaceId))
                .OrderBy(list => list.Id)
                .Select(list => new { list.Id, list.Name, list.BoardId, list.Position, list.IsArchived, list.UpdatedAt })
                .ToListAsync(),
            Cards = await database.Cards.AsNoTracking()
                .Where(card => database.Lists.Any(list => list.Id == card.ListId
                    && database.Boards.Any(board => board.Id == list.BoardId && board.WorkspaceId == place.WorkspaceId)))
                .OrderBy(card => card.Id)
                .Select(card => new
                {
                    card.Id,
                    card.Title,
                    card.ListId,
                    card.Position,
                    card.IsArchived,
                    card.CoverColor,
                    card.UpdatedAt,
                    Labels = card.Labels.OrderBy(label => label.Id).Select(label => label.Id).ToList()
                })
                .ToListAsync(),
            Labels = await database.Labels.AsNoTracking()
                .Where(label => database.Boards.Any(board => board.Id == label.BoardId && board.WorkspaceId == place.WorkspaceId))
                .OrderBy(label => label.Id)
                .Select(label => new { label.Id, label.Name, label.Color, label.UpdatedAt })
                .ToListAsync(),
            Checklists = await database.Checklists.AsNoTracking()
                .Where(checklist => checklist.CardId == place.Content.CardId)
                .OrderBy(checklist => checklist.Id)
                .Select(checklist => new { checklist.Id, checklist.Name, checklist.Position, checklist.IsArchived, checklist.UpdatedAt })
                .ToListAsync(),
            CheckItems = await database.CheckItems.AsNoTracking()
                .Where(checkItem => checkItem.ChecklistId == place.Content.ChecklistId)
                .OrderBy(checkItem => checkItem.Id)
                .Select(checkItem => new { checkItem.Id, checkItem.Name, checkItem.Position, checkItem.IsChecked, checkItem.UpdatedAt })
                .ToListAsync(),
            Attachments = await database.Attachments.AsNoTracking()
                .Where(attachment => attachment.CardId == place.Content.CardId)
                .OrderBy(attachment => attachment.Id)
                .Select(attachment => new { attachment.Id, attachment.Name, attachment.UpdatedAt })
                .ToListAsync(),
            Comments = await database.ActivityEntries.AsNoTracking()
                .Where(entry => entry.Type == ActivityType.Comment && entry.CardId == place.Content.CardId)
                .OrderBy(entry => entry.Id)
                .Select(entry => new { entry.Id, entry.Text, entry.UpdatedAt })
                .ToListAsync()
        }));

    protected async Task<Guid> SeedUserAsync()
    {
        var tag = Guid.NewGuid().ToString("n")[..12];
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"seeded-{tag}",
            Email = $"{tag}@example.com",
            PasswordHash = "unused",
            FirstName = "Wanjiru",
            LastName = "Otieno",
            Language = "en-US",
            TimeZone = "UTC",
            Location = "Kisumu",
            Verified = true
        };
        user.CreatedBy = user.Id;
        await FromDatabaseAsync(async database =>
        {
            database.Users.Add(user);
            return await database.SaveChangesAsync();
        });

        return user.Id;
    }

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
