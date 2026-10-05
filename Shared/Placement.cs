using FluentValidation;

namespace Laneway.Api;

public class PlacedEntity : BaseEntity
{
    public double Position { get; set; }
}

public interface IPlacing
{
    PositionValue? Position { get; }
    Guid? Before { get; }
    Guid? After { get; }
}

public sealed record Placement(PositionValue? Position, Guid? Before, Guid? After)
{
    public static Placement Of(IPlacing request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Placement(request.Position, request.Before, request.After);
    }

    public bool HasAnchor => Before is not null || After is not null;
    public bool IsEmpty => Position is null && !HasAnchor;
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

    public static void ValidPlacement<T>(this AbstractValidator<T> validator) where T : IPlacing
    {
        ArgumentNullException.ThrowIfNull(validator);

        validator.RuleFor(request => request.Position).ValidPosition();
        validator.RuleFor(request => request)
            .Must(request => Placement.Of(request) is { Position: null } or { HasAnchor: false })
            .WithErrorCode(ErrorCodes.MutuallyExclusive)
            .WithMessage(request => $"'position' can't be sent with {Anchors(Placement.Of(request))}.");
    }

    private static string Anchors(Placement placement) => placement switch
    {
        { Before: not null, After: not null } => "'before' and 'after'",
        { Before: not null } => "'before'",
        _ => "'after'"
    };
}
