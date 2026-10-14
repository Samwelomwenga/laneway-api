namespace Laneway.Api;

public static class ChecklistView
{
    public static ChecklistDto Of(Checklist checklist)
    {
        ArgumentNullException.ThrowIfNull(checklist);

        return new ChecklistDto(
            checklist.Id,
            checklist.CardId,
            checklist.Name,
            checklist.Position,
            checklist.IsArchived,
            CheckItemView.Sorted(checklist.CheckItems),
            checklist.CreatedAt,
            checklist.UpdatedAt,
            checklist.CreatedBy,
            checklist.UpdatedBy
        );
    }
}
