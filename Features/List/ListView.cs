namespace Laneway.Api;

public static class ListView
{
    public static ListDto Of(List list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return new ListDto(
            list.Id,
            list.Name,
            list.Position,
            list.BoardId,
            list.Color,
            list.IsArchived,
            list.CreatedAt,
            list.UpdatedAt,
            list.CreatedBy,
            list.UpdatedBy
        );
    }
}
