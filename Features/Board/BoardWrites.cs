namespace Laneway.Api;

public sealed record CreateBoardWrite(
    string Name,
    string Description,
    Guid WorkspaceId,
    BoardVisibility Visibility)
{
    public static CreateBoardWrite Of(CreateBoardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateBoardWrite(
            Writes.Required(dto.Name, "name"),
            dto.Description ?? string.Empty,
            Writes.Required(dto.WorkspaceId, "workspaceId"),
            Writes.Required(dto.Visibility, "visibility"));
    }
}

public sealed record UpdateBoardWrite(string Name, string Description, BoardVisibility Visibility)
{
    public static UpdateBoardWrite Of(UpdateBoardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateBoardWrite(
            Writes.Required(dto.Name, "name"),
            dto.Description ?? string.Empty,
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
