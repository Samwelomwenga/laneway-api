using System.Diagnostics.CodeAnalysis;

namespace Laneway.Api;

[SuppressMessage("Usage", "SYSLIB1025", Justification = "Variants of one event share its name.")]
public static partial class AccessLog
{
    public const string Category = "Laneway.Access";

    [LoggerMessage(EventId = 1, EventName = "MemberAdded", Level = LogLevel.Information,
        Message = "Member added: sub {sub}, {targetType} {targetId}, memberSub {memberSub}, role {role}")]
    private static partial void MemberAddedEvent(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid memberSub, string role);

    [LoggerMessage(EventId = 1, EventName = "MemberAdded", Level = LogLevel.Information,
        Message = "Member added: sub {sub}, {targetType} {targetId}, memberSub {memberSub}, role {role}, inviteId {inviteId}")]
    private static partial void MemberAddedByInvite(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid memberSub, string role, Guid inviteId);

    [LoggerMessage(EventId = 2, EventName = "MemberRoleChanged", Level = LogLevel.Information,
        Message = "Member role changed: sub {sub}, {targetType} {targetId}, memberSub {memberSub}, role {role}, previousRole {previousRole}")]
    private static partial void MemberRoleChangedEvent(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid memberSub, string role, string previousRole);

    [LoggerMessage(EventId = 3, EventName = "MemberRemoved", Level = LogLevel.Information,
        Message = "Member removed: sub {sub}, {targetType} {targetId}, memberSub {memberSub}, role {role}")]
    private static partial void MemberRemovedEvent(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid memberSub, string role);

    [LoggerMessage(EventId = 4, EventName = "InviteSent", Level = LogLevel.Information,
        Message = "Invite sent: sub {sub}, {targetType} {targetId}, inviteId {inviteId}, role {role}")]
    private static partial void InviteSentEvent(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid inviteId, string role);

    [LoggerMessage(EventId = 5, EventName = "InviteRevoked", Level = LogLevel.Information,
        Message = "Invite revoked: sub {sub}, {targetType} {targetId}, inviteId {inviteId}, role {role}")]
    private static partial void InviteRevokedEvent(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid inviteId, string role);

    [LoggerMessage(EventId = 6, EventName = "VisibilityChanged", Level = LogLevel.Information,
        Message = "Visibility changed: sub {sub}, {targetType} {targetId}, visibility {visibility}, previousVisibility {previousVisibility}")]
    private static partial void VisibilityChangedEvent(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, string visibility, string previousVisibility);

    [LoggerMessage(EventId = 7, EventName = "BoardSettingsChanged", Level = LogLevel.Information,
        Message = "Board settings changed: sub {sub}, {targetType} {targetId}, settings {settings}")]
    private static partial void BoardSettingsChangedEvent(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, string[] settings);

    [LoggerMessage(EventId = 8, EventName = "WorkspaceDeleted", Level = LogLevel.Information,
        Message = "Workspace deleted: sub {sub}, {targetType} {targetId}")]
    private static partial void WorkspaceDeletedEvent(ILogger logger, Guid sub, AccessTarget targetType, Guid targetId);

    [LoggerMessage(EventId = 9, EventName = "BoardDeleted", Level = LogLevel.Information,
        Message = "Board deleted: sub {sub}, {targetType} {targetId}")]
    private static partial void BoardDeletedEvent(ILogger logger, Guid sub, AccessTarget targetType, Guid targetId);

    public static void VisibilityChanged(
        this ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Enum visibility, Enum previousVisibility) =>
        VisibilityChangedEvent(logger, sub, targetType, targetId, visibility.ToString(), previousVisibility.ToString());

    public static void BoardSettingsChanged(this ILogger logger, Guid sub, Guid boardId, string[] settings) =>
        BoardSettingsChangedEvent(logger, sub, AccessTarget.Board, boardId, settings);

    public static void WorkspaceDeleted(this ILogger logger, Guid sub, Guid workspaceId) =>
        WorkspaceDeletedEvent(logger, sub, AccessTarget.Workspace, workspaceId);

    public static void BoardDeleted(this ILogger logger, Guid sub, Guid boardId) =>
        BoardDeletedEvent(logger, sub, AccessTarget.Board, boardId);

    public static void MemberAdded(
        this ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid memberSub, Enum role, Guid? inviteId = null)
    {
        if (inviteId is { } id)
        {
            MemberAddedByInvite(logger, sub, targetType, targetId, memberSub, role.ToString(), id);
        }
        else
        {
            MemberAddedEvent(logger, sub, targetType, targetId, memberSub, role.ToString());
        }
    }

    public static void MemberRoleChanged(
        this ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid memberSub, Enum role, Enum previousRole) =>
        MemberRoleChangedEvent(logger, sub, targetType, targetId, memberSub, role.ToString(), previousRole.ToString());

    public static void MemberRemoved(
        this ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, Guid memberSub, Enum role) =>
        MemberRemovedEvent(logger, sub, targetType, targetId, memberSub, role.ToString());

    public static void InviteSent(this ILogger logger, Guid sub, Invite invite) =>
        InviteSentEvent(logger, sub, TargetOf(invite), PlaceIdOf(invite), invite.Id, invite.Role);

    public static void InviteRevoked(this ILogger logger, Guid sub, Invite invite) =>
        InviteRevokedEvent(logger, sub, TargetOf(invite), PlaceIdOf(invite), invite.Id, invite.Role);

    private static AccessTarget TargetOf(Invite invite) =>
        invite.WorkspaceId is null ? AccessTarget.Board : AccessTarget.Workspace;

    private static Guid PlaceIdOf(Invite invite) =>
        invite.WorkspaceId ?? invite.BoardId ?? throw new InvalidOperationException($"Invite {invite.Id} has no place.");
}
