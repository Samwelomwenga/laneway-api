namespace Laneway.Api.Tests;

public enum Caller
{
    BoardAdmin,
    BoardNormal,
    BoardObserver,
    Guest,
    WorkspaceAdmin,
    WorkspaceNormal,
    Stranger
}

public enum Act
{
    ReadBoard,
    EditBoard,
    AdministerBoard,
    ReadWorkspace,
    AdministerWorkspace,
    CreateBoard
}

public sealed record Expect(int Status, string? Code, bool Limited)
{
    public override string ToString() =>
        Code is not null ? $"{Status} {Code}" : Limited ? $"{Status} limited" : $"{Status}";
}

// Columns follow the Caller order.
public static class AccessExpectations
{
    private static readonly Dictionary<(Act, BoardVisibility), string[]> Boards = new()
    {
        [(Act.ReadBoard, BoardVisibility.Private)] = ["ok", "ok", "ok", "ok", "404", "404", "404"],
        [(Act.ReadBoard, BoardVisibility.Workspace)] = ["ok", "ok", "ok", "ok", "ok", "ok", "404"],
        [(Act.ReadBoard, BoardVisibility.Public)] = ["ok", "ok", "ok", "ok", "ok", "ok", "ok"],

        [(Act.EditBoard, BoardVisibility.Private)] = ["ok", "ok", "readOnly", "ok", "404", "404", "404"],
        [(Act.EditBoard, BoardVisibility.Workspace)] = ["ok", "ok", "readOnly", "ok", "ok", "member", "404"],
        [(Act.EditBoard, BoardVisibility.Public)] = ["ok", "ok", "readOnly", "ok", "ok", "member", "member"],

        [(Act.AdministerBoard, BoardVisibility.Private)] = ["ok", "admin", "readOnly", "admin", "404", "404", "404"],
        [(Act.AdministerBoard, BoardVisibility.Workspace)] = ["ok", "admin", "readOnly", "admin", "ok", "member", "404"],
        [(Act.AdministerBoard, BoardVisibility.Public)] = ["ok", "admin", "readOnly", "admin", "ok", "member", "member"],
    };

    private static readonly Dictionary<(Act, WorkspaceVisibility), string[]> Workspaces = new()
    {
        [(Act.ReadWorkspace, WorkspaceVisibility.Private)] = ["ok", "ok", "ok", "limited", "ok", "ok", "404"],
        [(Act.ReadWorkspace, WorkspaceVisibility.Public)] = ["ok", "ok", "ok", "limited", "ok", "ok", "limited"],

        [(Act.AdministerWorkspace, WorkspaceVisibility.Private)] = ["wsAdmin", "wsAdmin", "wsAdmin", "wsAdmin", "ok", "wsAdmin", "404"],
        [(Act.AdministerWorkspace, WorkspaceVisibility.Public)] = ["wsAdmin", "wsAdmin", "wsAdmin", "wsAdmin", "ok", "wsAdmin", "wsAdmin"],

        [(Act.CreateBoard, WorkspaceVisibility.Private)] = ["ok", "ok", "ok", "wsMember", "ok", "ok", "404"],
        [(Act.CreateBoard, WorkspaceVisibility.Public)] = ["ok", "ok", "ok", "wsMember", "ok", "ok", "wsMember"],
    };

    public static Expect For(Act act, Caller caller, BoardVisibility visibility, int success) =>
        Parse(Boards[(act, visibility)][(int)caller], success);

    public static Expect For(Act act, Caller caller, WorkspaceVisibility visibility, int success) =>
        Parse(Workspaces[(act, visibility)][(int)caller], success);

    private static Expect Parse(string cell, int success) => cell switch
    {
        "ok" => new Expect(success, null, false),
        "limited" => new Expect(200, null, true),
        "404" => new Expect(404, null, false),
        "member" => new Expect(403, ErrorCodes.RequiresBoardMember, false),
        "readOnly" => new Expect(403, ErrorCodes.ReadOnlyRole, false),
        "admin" => new Expect(403, ErrorCodes.RequiresBoardAdmin, false),
        "wsMember" => new Expect(403, ErrorCodes.RequiresWorkspaceMember, false),
        "wsAdmin" => new Expect(403, ErrorCodes.RequiresWorkspaceAdmin, false),
        _ => throw new ArgumentOutOfRangeException(nameof(cell), cell, null)
    };
}
