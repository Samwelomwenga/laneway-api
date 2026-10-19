namespace Laneway.Api;

public enum BoardAction
{
    Edit,
    Comment,
    Administer,
    AddMember,
    GrantAdmin,
    SelfJoin
}

public enum WorkspaceAction
{
    CreateBoard,
    ListMembers,
    Administer
}

public sealed record Refusal(string Code, string Message);

public static class Refusals
{
    public static readonly Refusal RequiresBoardMember =
        new(ErrorCodes.RequiresBoardMember, "Join this board to change it.");

    public static readonly Refusal ReadOnlyRole =
        new(ErrorCodes.ReadOnlyRole, "Observers only read this board. Ask a board admin to make you a normal member.");

    public static readonly Refusal RequiresBoardAdmin =
        new(ErrorCodes.RequiresBoardAdmin, "Only a board admin can do this. Ask one to do it for you.");

    public static readonly Refusal RequiresWorkspaceMember =
        new(ErrorCodes.RequiresWorkspaceMember, "Join this workspace to do this.");

    public static readonly Refusal RequiresWorkspaceAdmin =
        new(ErrorCodes.RequiresWorkspaceAdmin, "Only a workspace admin can do this. Ask one to do it for you.");

    public static readonly Refusal NotAuthor =
        new(ErrorCodes.NotAuthor, "Only the author can edit or delete a comment.");

    public static readonly Refusal CommentingDisabled =
        new(ErrorCodes.CommentingDisabled, "Comments are turned off on this board. A board admin can turn them on.");
}

public static class BoardRules
{
    public static Refusal? Refusal(BoardStanding standing, BoardAction action)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return action switch
        {
            BoardAction.Edit => AtLeast(standing, Role.Normal),
            BoardAction.Administer or BoardAction.GrantAdmin => AtLeast(standing, Role.Admin),
            BoardAction.AddMember => AtLeast(
                standing, standing.AddingMembers == BoardAddingMembers.Members ? Role.Normal : Role.Admin),
            BoardAction.Comment => Commenting(standing),
            BoardAction.SelfJoin => SelfJoining(standing),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static Refusal? AtLeast(BoardStanding standing, Role needed)
    {
        if (standing.Role >= needed)
        {
            return null;
        }

        if (!standing.IsMember)
        {
            return Refusals.RequiresBoardMember;
        }

        return standing.Role == Role.Observer
            ? Refusals.ReadOnlyRole
            : Refusals.RequiresBoardAdmin;
    }

    private static Refusal? Commenting(BoardStanding standing) => standing.Commenting switch
    {
        BoardCommenting.Disabled => Refusals.CommentingDisabled,
        BoardCommenting.Members => AtLeast(standing, Role.Normal),
        BoardCommenting.Observers => standing.IsMember || standing.Role >= Role.Normal
            ? null
            : Refusals.RequiresBoardMember,
        BoardCommenting.Workspace => standing.IsMember || standing.IsWorkspaceMember || standing.Role >= Role.Normal
            ? null
            : Refusals.RequiresWorkspaceMember,
        BoardCommenting.Public => null,
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing.Commenting, null)
    };

    private static Refusal? SelfJoining(BoardStanding standing)
    {
        if (!standing.SelfJoin)
        {
            return Refusals.RequiresBoardAdmin;
        }

        return standing.IsWorkspaceMember ? null : Refusals.RequiresWorkspaceMember;
    }
}

public static class WorkspaceRules
{
    public static Refusal? Refusal(WorkspaceStanding standing, WorkspaceAction action)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return action switch
        {
            WorkspaceAction.CreateBoard or WorkspaceAction.ListMembers =>
                standing.IsMember ? null : Refusals.RequiresWorkspaceMember,
            WorkspaceAction.Administer => standing.Role == Role.Admin
                ? null
                : Refusals.RequiresWorkspaceAdmin,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }
}
