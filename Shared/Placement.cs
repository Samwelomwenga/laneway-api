using FluentValidation;

namespace DefaultNamespace;

public class PlacedEntity : BaseEntity
{
    public double Position { get; set; }
}

public sealed record Placement(PositionValue? Position, Guid? Before, Guid? After)
{
    public bool HasAnchor => Before is not null || After is not null;
}

public sealed record PlacementResult(double Position, List<ApiError> Errors);

public static class SortOrder
{
    public static IOrderedQueryable<T> InSortOrder<T>(this IQueryable<T> query) where T : PlacedEntity =>
        query.OrderBy(item => item.Position).ThenBy(item => item.CreatedAt).ThenBy(item => item.Id);

    public static IOrderedEnumerable<T> InSortOrder<T>(this IEnumerable<T> items) where T : PlacedEntity =>
        items.OrderBy(item => item.Position).ThenBy(item => item.CreatedAt).ThenBy(item => item.Id);
}

public static class PlacementRules
{
    public static IRuleBuilderOptions<T, PositionValue?> ValidPosition<T>(
        this IRuleBuilder<T, PositionValue?> rule) =>
        rule.Must(position => position?.ErrorCode != ErrorCodes.UnknownValue)
            .WithErrorCode(ErrorCodes.UnknownValue)
            .WithMessage("'{PropertyName}' must be 'top', 'bottom', or a number above 0.")
            .Must(position => position?.ErrorCode != ErrorCodes.OutOfRange)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage("'{PropertyName}' must be above 0.");

    public static IRuleBuilderOptions<T, T> OnePlacement<T>(
        this IRuleBuilder<T, T> rule, Func<T, Placement> placement) =>
        rule.Must(request => placement(request) is { Position: null } or { HasAnchor: false })
            .WithErrorCode(ErrorCodes.MutuallyExclusive)
            .WithMessage(request => $"'position' can't be sent with {Anchors(placement(request))}.");

    private static string Anchors(Placement placement) => placement switch
    {
        { Before: not null, After: not null } => "'before' and 'after'",
        { Before: not null } => "'before'",
        _ => "'after'"
    };
}
