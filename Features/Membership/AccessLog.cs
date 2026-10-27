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
