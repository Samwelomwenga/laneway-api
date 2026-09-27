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

public sealed record CreateWorkspaceData(ActorRef Actor, WorkspaceRef Workspace);

public sealed record UpdateWorkspaceData(ActorRef Actor, WorkspaceRef Workspace, WorkspaceFields Old);

public sealed record DeleteWorkspaceData(ActorRef Actor, WorkspaceRef Workspace);

public sealed record WorkspaceFields(string? Name, string? Description, WorkspaceVisibility? Visibility)
{
    public static WorkspaceFields Changed(EntityEntry<WorkSpace> tracked) => new(
        tracked.Old(workspace => workspace.Name),
        tracked.Old(workspace => workspace.Description),
        tracked.Old(workspace => workspace.Visibility));
}
