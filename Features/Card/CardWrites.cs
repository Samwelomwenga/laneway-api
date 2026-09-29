namespace DefaultNamespace;

public sealed record CreateCardWrite(
    string Title,
    string Description,
    DateTime? DueDate,
    Guid ListId,
    bool IsDueComplete,
    DateTime? StartDate,
    int? DueReminderMinutes,
    List<Guid>? LabelIds,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static CreateCardWrite Of(CreateCardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateCardWrite(
            Writes.Required(dto.Title, "title"),
            dto.Description ?? string.Empty,
            dto.DueDate,
            Writes.Required(dto.ListId, "listId"),
            Writes.Required(dto.IsDueComplete, "isDueComplete"),
            dto.StartDate,
            dto.DueReminderMinutes,
            Writes.EachRequired(dto.LabelIds, "labelIds"),
            dto.Position,
            dto.Before,
            dto.After);
    }
}

public sealed record UpdateCardWrite(
    string Title,
    string Description,
    DateTime? DueDate,
    bool IsDueComplete,
    DateTime? StartDate,
    int? DueReminderMinutes,
    List<Guid>? LabelIds)
{
    public static UpdateCardWrite Of(UpdateCardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateCardWrite(
            Writes.Required(dto.Title, "title"),
            dto.Description ?? string.Empty,
            dto.DueDate,
            Writes.Required(dto.IsDueComplete, "isDueComplete"),
            dto.StartDate,
            dto.DueReminderMinutes,
            Writes.EachRequired(dto.LabelIds, "labelIds"));
    }
}

public sealed record MoveCardWrite(
    Guid ListId,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static MoveCardWrite Of(MoveCardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new MoveCardWrite(
            Writes.Required(dto.ListId, "listId"),
            dto.Position,
            dto.Before,
            dto.After);
    }
}

public sealed record CopyCardWrite(
    Guid ListId,
    string? Title,
    List<CopyPart>? Keep,
    PositionValue? Position,
    Guid? Before,
    Guid? After) : IPlacing
{
    public static CopyCardWrite Of(CopyCardDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CopyCardWrite(
            Writes.Required(dto.ListId, "listId"),
            dto.Title,
            dto.Keep,
            dto.Position,
            dto.Before,
            dto.After);
    }
}
