namespace Laneway.Api;

public static class LabelView
{
    public static LabelDto Of(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);

        return new LabelDto
        (
            label.Id,
            label.Name,
            label.BoardId,
            label.Color,
            label.CreatedAt,
            label.UpdatedAt,
            label.CreatedBy,
            label.UpdatedBy
        );
    }

    public static IOrderedQueryable<Label> InNameOrder(this IQueryable<Label> labels) =>
        labels
            .OrderBy(label => label.Name)
            .ThenBy(label => label.Color)
            .ThenBy(label => label.CreatedAt)
            .ThenBy(label => label.Id);
}
