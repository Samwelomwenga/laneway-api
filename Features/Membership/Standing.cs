namespace Laneway.Api;

public enum Role
{
    None,
    Observer,
    Normal,
    Admin
}

public sealed record BoardStanding(
    Role Role,
    bool IsMember,
    bool IsWorkspaceMember,
    BoardCommenting Commenting,
    BoardAddingMembers AddingMembers,
    bool SelfJoin)
{
    public static BoardStanding Of(Board board, BoardRole? boardRole, WorkspaceRole? workspaceRole)
    {
        ArgumentNullException.ThrowIfNull(board);

        var granted = workspaceRole == WorkspaceRole.Admin && board.Visibility != BoardVisibility.Private
            ? Role.Admin
            : Role.None;
        var stored = boardRole switch
        {
            BoardRole.Admin => Role.Admin,
            BoardRole.Normal => Role.Normal,
            BoardRole.Observer => Role.Observer,
            _ => Role.None
        };

        return new BoardStanding(
            stored > granted ? stored : granted,
            boardRole is not null,
            workspaceRole is not null,
            board.Commenting,
            board.AddingMembers,
            board.SelfJoin);
    }
}

public sealed record WorkspaceStanding(Role Role)
{
    public bool IsMember => Role != Role.None;

    public static WorkspaceStanding Of(WorkspaceRole? role) => new(role switch
    {
        WorkspaceRole.Admin => Role.Admin,
        WorkspaceRole.Normal => Role.Normal,
        _ => Role.None
    });
}

public sealed record Found<TTarget, TStanding>(TTarget Target, TStanding Standing);
