using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DefaultNamespace;

public sealed record ActorRef(Guid Id, string Username, string FirstName, string LastName);

public sealed record WorkspaceRef(Guid Id, string Name)
{
    public static WorkspaceRef Of(WorkSpace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return new WorkspaceRef(workspace.Id, workspace.Name);
    }
}

public sealed record BoardRef(Guid Id, string Name)
{
    public static BoardRef Of(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return new BoardRef(board.Id, board.Name);
    }
}

public sealed record ListRef(Guid Id, string Name, Color? Color)
{
    public static ListRef Of(List list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new ListRef(list.Id, list.Name, list.Color);
    }
}

public sealed record LabelRef(Guid Id, string Name, Color? Color)
{
    public static LabelRef Of(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new LabelRef(label.Id, label.Name, label.Color);
    }
}

public sealed record CreateWorkspaceData(ActorRef Actor, WorkspaceRef Workspace);

public sealed record UpdateWorkspaceData(ActorRef Actor, WorkspaceRef Workspace, WorkspaceFields Old);

public sealed record DeleteWorkspaceData(ActorRef Actor, WorkspaceRef Workspace);

public sealed record WorkspaceFields(
    Was<string>? Name, Was<string>? Description, Was<WorkspaceVisibility>? Visibility)
{
    public static WorkspaceFields Changed(EntityEntry<WorkSpace> tracked) => new(
        tracked.Old(workspace => workspace.Name),
        tracked.Old(workspace => workspace.Description),
        tracked.Old(workspace => workspace.Visibility));
}

public sealed record CreateBoardData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board);

public sealed record UpdateBoardData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, BoardFields Old);

public sealed record ArchiveBoardData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board);

public sealed record MoveBoardData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, BoardOrigin From);

public sealed record DeleteBoardData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board);

public sealed record BoardOrigin(WorkspaceRef Workspace);

public sealed record BoardFields(Was<string>? Name, Was<string>? Description, Was<BoardVisibility>? Visibility)
{
    public static BoardFields Changed(EntityEntry<Board> tracked) => new(
        tracked.Old(board => board.Name),
        tracked.Old(board => board.Description),
        tracked.Old(board => board.Visibility));
}

public sealed record CreateListData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List);

public sealed record UpdateListData(
    ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List, ListFields Old);

public sealed record ArchiveListData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List);

public sealed record MoveListData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    PositionChange Position,
    ListOrigin? From,
    IReadOnlyList<LabelRef>? CreatedLabels);

public sealed record DeleteListData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List);

public sealed record ListOrigin(WorkspaceRef? Workspace, BoardRef Board);

public sealed record PositionChange(double Old, double New);

public sealed record ListFields(Was<string>? Name, Was<Color?>? Color)
{
    public static ListFields Changed(EntityEntry<List> tracked) => new(
        tracked.Old(list => list.Name),
        tracked.Old(list => list.Color));
}

public sealed record CreateLabelData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, LabelRef Label);

public sealed record UpdateLabelData(
    ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, LabelRef Label, LabelFields Old);

public sealed record DeleteLabelData(ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, LabelRef Label);

public sealed record LabelFields(Was<string>? Name, Was<Color?>? Color)
{
    public static LabelFields Changed(EntityEntry<Label> tracked) => new(
        tracked.Old(label => label.Name),
        tracked.Old(label => label.Color));
}
