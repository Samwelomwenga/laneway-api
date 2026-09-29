namespace DefaultNamespace;

public sealed record CreateChecklistWrite(
    string Name,
    Guid CardId,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static CreateChecklistWrite Of(CreateChecklistDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateChecklistWrite(
            Writes.Required(dto.Name, "name"),
            Writes.Required(dto.CardId, "cardId"),
            dto.Position,
            dto.Before,
            dto.After);
    }
}

public sealed record UpdateChecklistWrite(string Name)
{
    public static UpdateChecklistWrite Of(UpdateChecklistDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateChecklistWrite(Writes.Required(dto.Name, "name"));
    }
}
