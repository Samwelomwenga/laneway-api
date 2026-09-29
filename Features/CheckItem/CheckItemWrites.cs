namespace DefaultNamespace;

public sealed record CreateCheckItemWrite(
    string Name,
    bool IsChecked,
    Guid ChecklistId,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static CreateCheckItemWrite Of(CreateCheckItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateCheckItemWrite(
            Writes.Required(dto.Name, "name"),
            dto.IsChecked ?? false,
            Writes.Required(dto.ChecklistId, "checklistId"),
            dto.Position,
            dto.Before,
            dto.After);
    }
}

public sealed record UpdateCheckItemWrite(string Name)
{
    public static UpdateCheckItemWrite Of(UpdateCheckItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateCheckItemWrite(Writes.Required(dto.Name, "name"));
    }
}

public sealed record MoveCheckItemWrite(
    Guid ChecklistId,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static MoveCheckItemWrite Of(MoveCheckItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new MoveCheckItemWrite(
            Writes.Required(dto.ChecklistId, "checklistId"),
            dto.Position,
            dto.Before,
            dto.After);
    }
}

public sealed record CheckedWrite(bool Value)
{
    public static CheckedWrite Of(CheckedDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CheckedWrite(Writes.Required(dto.Value, "value"));
    }
}
