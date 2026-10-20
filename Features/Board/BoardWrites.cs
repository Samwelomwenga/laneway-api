namespace Laneway.Api;

public sealed record CreateBoardWrite(
    string Name,
    string Description,
    Guid WorkspaceId,
    BoardSettingsWrite Settings)
{
    public static CreateBoardWrite Of(Guid workspaceId, CreateBoardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateBoardWrite(
            Writes.Required(dto.Name, "name"),
            dto.Description ?? string.Empty,
            workspaceId,
            new BoardSettingsWrite(
                dto.Commenting ?? BoardCommenting.Members,
                dto.AddingMembers ?? BoardAddingMembers.Admins,
                dto.SelfJoin ?? true,
                dto.Visibility ?? BoardVisibility.Private));
    }
}

public sealed record UpdateBoardWrite(string Name, string Description)
{
    public static UpdateBoardWrite Of(UpdateBoardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateBoardWrite(Writes.Required(dto.Name, "name"), dto.Description ?? string.Empty);
    }
}

public sealed record BoardSettingsWrite(
    BoardCommenting Commenting,
    BoardAddingMembers AddingMembers,
    bool SelfJoin,
    BoardVisibility Visibility)
{
    public static BoardSettingsWrite Of(UpdateBoardSettingsDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new BoardSettingsWrite(
            Writes.Required(dto.Commenting, "commenting"),
            Writes.Required(dto.AddingMembers, "addingMembers"),
            Writes.Required(dto.SelfJoin, "selfJoin"),
            Writes.Required(dto.Visibility, "visibility"));
    }
}

public sealed record MoveBoardWrite(Guid WorkspaceId)
{
    public static MoveBoardWrite Of(MoveBoardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new MoveBoardWrite(Writes.Required(dto.WorkspaceId, "workspaceId"));
    }
}

public sealed record CopyBoardWrite(Guid WorkspaceId, string? Name, List<BoardCopyPart>? Keep)
{
    public static CopyBoardWrite Of(CopyBoardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CopyBoardWrite(
            Writes.Required(dto.WorkspaceId, "workspaceId"),
            dto.Name,
            dto.Keep);
    }
}
