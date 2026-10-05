namespace Laneway.Api;

public sealed record CreateListWrite(
    string Name,
    Guid BoardId,
    Color? Color,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static CreateListWrite Of(CreateListDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateListWrite(
            Writes.Required(dto.Name, "name"),
            Writes.Required(dto.BoardId, "boardId"),
            dto.Color,
            dto.Position,
            dto.Before,
            dto.After);
    }
}

public sealed record UpdateListWrite(string Name, Color? Color)
{
    public static UpdateListWrite Of(UpdateListDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateListWrite(Writes.Required(dto.Name, "name"), dto.Color);
    }
}

public sealed record MoveListWrite(
    Guid BoardId,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static MoveListWrite Of(MoveListDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new MoveListWrite(
            Writes.Required(dto.BoardId, "boardId"),
            dto.Position,
            dto.Before,
            dto.After);
    }
}

public sealed record CopyListWrite(
    Guid BoardId,
    string? Name,
    List<CopyPart>? Keep,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static CopyListWrite Of(CopyListDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CopyListWrite(
            Writes.Required(dto.BoardId, "boardId"),
            dto.Name,
            dto.Keep,
            dto.Position,
            dto.Before,
            dto.After);
    }
}
