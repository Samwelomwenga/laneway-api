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

public sealed record CopyBoardData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    BoardRef Source,
    BoardOrigin? From,
    IReadOnlyList<BoardCopyPart> Keep);

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

public sealed record ListOrigin(WorkspaceRef? Workspace, BoardRef Board)
{
    public static ListOrigin Between(BoardChain from, BoardChain to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return new ListOrigin(from.Workspace.Id == to.Workspace.Id ? null : from.Workspace, from.Board);
    }
}

public sealed record CopyListData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    ListRef Source,
    ListOrigin? From,
    IReadOnlyList<CopyPart> Keep,
    IReadOnlyList<LabelRef>? CreatedLabels);

public sealed record PositionChange(double Old, double New);

public sealed record CompletionChange(bool Old, bool New);

public sealed record ListFields(Was<string>? Name, Was<Color?>? Color)
{
    public static ListFields Changed(EntityEntry<List> tracked) => new(
        tracked.Old(list => list.Name),
        tracked.Old(list => list.Color));
}

public sealed record CardRef(Guid Id, string Title)
{
    public static CardRef Of(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new CardRef(card.Id, card.Title);
    }
}

public sealed record CreateCardData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    IReadOnlyList<LabelRef>? Labels);

public sealed record UpdateCardData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    IReadOnlyList<LabelRef>? Labels,
    CardFields Old,
    CompletionChange? Completion);

public sealed record ArchiveCardData(
    ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List, CardRef Card);

public sealed record MoveCardData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    PositionChange Position,
    CardOrigin? From,
    IReadOnlyList<LabelSwap>? LabelSwaps);

public sealed record DeleteCardData(
    ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List, CardRef Card);

public sealed record CardLabelData(
    ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List, CardRef Card, LabelRef Label);

public sealed record CardOrigin(WorkspaceRef? Workspace, BoardRef? Board, ListRef List)
{
    public static CardOrigin Between(ListChain from, ListChain to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return new CardOrigin(
            from.Workspace.Id == to.Workspace.Id ? null : from.Workspace,
            from.Board.Id == to.Board.Id ? null : from.Board,
            from.List);
    }
}

public sealed record LabelSwap(LabelRef From, LabelRef To, bool Created);

public sealed record CopyCardData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    CardRef Source,
    CardOrigin? From,
    IReadOnlyList<CopyPart> Keep,
    IReadOnlyList<LabelSwap>? LabelSwaps);

public sealed record CardFields(
    Was<string>? Title,
    Was<string>? Description,
    Was<DateTime?>? DueDate,
    Was<bool>? IsDueComplete,
    Was<DateTime?>? StartDate,
    Was<int?>? DueReminderMinutes,
    Was<IReadOnlyList<LabelRef>>? Labels)
{
    public static CardFields Changed(EntityEntry<Card> tracked, IReadOnlyList<LabelRef>? oldLabels) => new(
        tracked.Old(card => card.Title),
        tracked.Old(card => card.Description),
        tracked.Old(card => card.DueDate),
        tracked.Old(card => card.IsDueComplete),
        tracked.Old(card => card.StartDate),
        tracked.Old(card => card.DueReminderMinutes),
        oldLabels is null ? null : new Was<IReadOnlyList<LabelRef>>(oldLabels));
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

public sealed record ChecklistRef(Guid Id, string Name)
{
    public static ChecklistRef Of(Checklist checklist)
    {
        ArgumentNullException.ThrowIfNull(checklist);
        return new ChecklistRef(checklist.Id, checklist.Name);
    }
}

public sealed record CheckItemRef(Guid Id, string Name)
{
    public static CheckItemRef Of(CheckItem checkItem)
    {
        ArgumentNullException.ThrowIfNull(checkItem);
        return new CheckItemRef(checkItem.Id, checkItem.Name);
    }
}

public sealed record CreateChecklistData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist);

public sealed record UpdateChecklistData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    ChecklistFields Old);

public sealed record MoveChecklistData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    PositionChange Position);

public sealed record ArchiveChecklistData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    CompletionChange? Completion);

public sealed record DeleteChecklistData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist);

public sealed record ChecklistFields(Was<string>? Name)
{
    public static ChecklistFields Changed(EntityEntry<Checklist> tracked) => new(
        tracked.Old(checklist => checklist.Name));
}

public sealed record CreateCheckItemData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    CheckItemRef CheckItem,
    CompletionChange? Completion);

public sealed record UpdateCheckItemData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    CheckItemRef CheckItem,
    CheckItemFields Old);

public sealed record MoveCheckItemData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    CheckItemRef CheckItem,
    PositionChange Position,
    CheckItemOrigin? From);

public sealed record CheckedCheckItemData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    CheckItemRef CheckItem,
    CompletionChange? Completion);

public sealed record DeleteCheckItemData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    ChecklistRef Checklist,
    CheckItemRef CheckItem,
    CompletionChange? Completion);

public sealed record CheckItemOrigin(ChecklistRef Checklist);

public sealed record CheckItemFields(Was<string>? Name)
{
    public static CheckItemFields Changed(EntityEntry<CheckItem> tracked) => new(
        tracked.Old(checkItem => checkItem.Name));
}

public sealed record AttachmentRef(Guid Id, string Name, AttachmentKind Kind)
{
    public static AttachmentRef Of(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return new AttachmentRef(attachment.Id, attachment.Name, attachment.Kind);
    }
}

public sealed record AddAttachmentData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    AttachmentRef Attachment);

public sealed record UpdateAttachmentData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    AttachmentRef Attachment,
    AttachmentFields Old);

public sealed record DeleteAttachmentData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    AttachmentRef Attachment,
    bool? WasCover);

public sealed record AttachmentFields(Was<string>? Name)
{
    public static AttachmentFields Changed(EntityEntry<Attachment> tracked) => new(
        tracked.Old(attachment => attachment.Name));
}

public sealed record CoverRef(AttachmentRef? Attachment, Color? Color)
{
    public static CoverRef? Of(AttachmentRef? attachment, Color? color) =>
        attachment is null && color is null ? null : new CoverRef(attachment, color);
}

public sealed record UpdateCardCoverData(
    ActorRef Actor,
    WorkspaceRef Workspace,
    BoardRef Board,
    ListRef List,
    CardRef Card,
    CoverRef? Cover,
    CoverRef? Old);

public sealed record CommentData(
    ActorRef Actor, WorkspaceRef Workspace, BoardRef Board, ListRef List, CardRef Card);
