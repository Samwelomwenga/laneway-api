namespace DefaultNamespace;

public readonly record struct ActivityPlace(
    Guid? WorkspaceId = null,
    Guid? BoardId = null,
    Guid? ListId = null,
    Guid? CardId = null,
    Guid? FromWorkspaceId = null,
    Guid? FromBoardId = null,
    Guid? FromListId = null)
{
    public static ActivityPlace OnWorkspace(Guid workspaceId) => new(WorkspaceId: workspaceId);
}
