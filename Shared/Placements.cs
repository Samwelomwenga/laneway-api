using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed class Placements
{
    public const double Gap = 65536;

    private readonly ApplicationDbContext _context;

    public Placements(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PlacementResult> ResolveInListAsync(Guid listId, Placement placement)
    {
        await _context.LockListsAsync(listId);
        return await InListAsync(listId, placement, moving: null);
    }

    public async Task<PlacementResult> ResolveMoveInListAsync(Card card, Guid listId, Placement placement)
    {
        ArgumentNullException.ThrowIfNull(card);

        await _context.LockListsAsync(card.ListId, listId);
        return await InListAsync(listId, placement, card);
    }

    public async Task<PlacementResult> ResolveOnBoardAsync(Guid boardId, Placement placement)
    {
        await _context.TryLockBoardAsync(boardId);

        var lists = await _context.Lists.Where(list => list.BoardId == boardId).InSortOrder().ToListAsync();
        var siblings = lists.ConvertAll(list => new Sibling(list, list.IsArchived));

        return await ResolveAsync(siblings, placement, moving: null, "List", "board",
            id => _context.Lists.AnyAsync(list => list.Id == id));
    }

    private async Task<PlacementResult> InListAsync(Guid listId, Placement placement, Card? moving)
    {
        var cards = await _context.Cards.Where(card => card.ListId == listId).InSortOrder().ToListAsync();
        var siblings = cards.ConvertAll(card => new Sibling(card, card.IsArchived));

        return await ResolveAsync(siblings, placement, moving, "Card", "list",
            id => _context.Cards.AnyAsync(card => card.Id == id));
    }

    private static async Task<PlacementResult> ResolveAsync(
        List<Sibling> siblings,
        Placement placement,
        PlacedEntity? moving,
        string resource,
        string container,
        Func<Guid, Task<bool>> existsAsync)
    {
        if (moving is not null)
        {
            var staying = siblings.RemoveAll(sibling => sibling.Id == moving.Id) > 0;
            if (staying && placement.IsEmpty)
            {
                return new PlacementResult(moving.Position, []);
            }
        }

        var errors = new List<ApiError>();
        var after = await AnchorAsync(placement.After, "after", siblings, resource, container, existsAsync, errors);
        var before = await AnchorAsync(placement.Before, "before", siblings, resource, container, existsAsync, errors);
        if (errors.Count > 0)
        {
            return new PlacementResult(0, errors);
        }

        if (after is not null && before is not null && !AreNeighbours(siblings, after, before))
        {
            errors.Add(new ApiError("before", ErrorCodes.NotAdjacent,
                $"'before' must name the {resource.ToLowerInvariant()} right after 'after'."));
            return new PlacementResult(0, errors);
        }

        var (position, index) = ResolvePosition(siblings, placement, after, before);
        return new PlacementResult(position ?? Respread(siblings, index), errors);
    }

    private static async Task<Sibling?> AnchorAsync(
        Guid? anchor,
        string field,
        List<Sibling> siblings,
        string resource,
        string container,
        Func<Guid, Task<bool>> existsAsync,
        List<ApiError> errors)
    {
        if (anchor is not { } id)
        {
            return null;
        }

        if (siblings.Find(sibling => sibling.Id == id) is { } sibling)
        {
            return sibling;
        }

        errors.Add(await existsAsync(id)
            ? new ApiError(field, ErrorCodes.NotSibling, $"{resource} {id} isn't in the same {container}.")
            : new ApiError(field, ErrorCodes.NotFound, $"{resource} {id} does not exist."));
        return null;
    }

    private static bool AreNeighbours(List<Sibling> siblings, Sibling after, Sibling before)
    {
        var start = siblings.IndexOf(after);
        var end = siblings.IndexOf(before);
        return start < end
               && siblings.GetRange(start + 1, end - start - 1).TrueForAll(sibling => sibling.IsArchived);
    }

    private static (double? Position, int Index) ResolvePosition(
        List<Sibling> siblings, Placement placement, Sibling? after, Sibling? before)
    {
        if (placement.Position?.Number is { } number)
        {
            return (number, siblings.Count(sibling => sibling.Position <= number));
        }

        if (after is not null)
        {
            var index = siblings.IndexOf(after) + 1;
            var next = before ?? siblings.ElementAtOrDefault(index);
            return (next is null ? Above(after.Position) : Between(after.Position, next.Position), index);
        }

        if (before is not null)
        {
            var index = siblings.IndexOf(before);
            var previous = siblings.ElementAtOrDefault(index - 1);
            return (previous is null ? Below(before.Position) : Between(previous.Position, before.Position), index);
        }

        if (placement.Position?.Edge == PositionEdge.Top)
        {
            return (siblings.Count == 0 ? Gap : Below(siblings[0].Position), 0);
        }

        return (siblings.Count == 0 ? Gap : Above(siblings[^1].Position), siblings.Count);
    }

    private static double? Between(double low, double high)
    {
        var middle = low + (high - low) / 2;
        return low < middle && middle < high ? middle : null;
    }

    private static double? Above(double largest)
    {
        var next = largest + Gap;
        return next > largest ? next : null;
    }

    private static double? Below(double smallest)
    {
        var half = smallest / 2;
        return half > 0 && half < smallest ? half : null;
    }

    private static double Respread(List<Sibling> siblings, int index)
    {
        for (var rank = 0; rank < siblings.Count; rank++)
        {
            var slot = rank < index ? rank : rank + 1;
            siblings[rank].Entity.Position = Gap * (slot + 1);
        }

        return Gap * (index + 1);
    }

    private sealed record Sibling(PlacedEntity Entity, bool IsArchived)
    {
        public Guid Id => Entity.Id;
        public double Position => Entity.Position;
    }
}
