namespace DefaultNamespace;

public static class ActivityView
{
    public static ActivityEntryDto Of(ActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ActivityEntryDto
        (
            entry.Id,
            entry.Type,
            entry.CreatedAt,
            entry.CreatedBy,
            entry.WorkspaceId,
            entry.BoardId,
            entry.ListId,
            entry.CardId,
            entry.FromWorkspaceId,
            entry.FromBoardId,
            entry.FromListId,
            entry.Data.RootElement,
            entry.Text,
            entry.UpdatedAt
        );
    }
}
