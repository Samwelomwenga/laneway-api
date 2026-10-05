namespace Laneway.Api;

public sealed record CreateWorkSpaceWrite(string Name, string Description, WorkspaceVisibility Visibility)
{
    public static CreateWorkSpaceWrite Of(CreateWorkSpaceDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateWorkSpaceWrite(
            Writes.Required(dto.Name, "name"),
            dto.Description ?? string.Empty,
            Writes.Required(dto.Visibility, "visibility"));
    }
}

public sealed record UpdateWorkSpaceWrite(string Name, string Description, WorkspaceVisibility Visibility)
{
    public static UpdateWorkSpaceWrite Of(UpdateWorkSpaceDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateWorkSpaceWrite(
            Writes.Required(dto.Name, "name"),
            dto.Description ?? string.Empty,
            Writes.Required(dto.Visibility, "visibility"));
    }
}
