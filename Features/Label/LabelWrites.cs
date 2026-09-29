namespace DefaultNamespace;

public sealed record CreateLabelWrite(string Name, Guid BoardId, Color? Color)
{
    public static CreateLabelWrite Of(CreateLabelDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateLabelWrite(
            dto.Name ?? string.Empty,
            Writes.Required(dto.BoardId, "boardId"),
            dto.Color);
    }
}

public sealed record UpdateLabelWrite(string Name, Color? Color)
{
    public static UpdateLabelWrite Of(UpdateLabelDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateLabelWrite(dto.Name ?? string.Empty, dto.Color);
    }
}
