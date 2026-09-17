using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed record LabelMatch(Label From, Label To, bool Created);

public sealed class LabelMatching
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;

    public LabelMatching(ApplicationDbContext context, Actor actor)
    {
        _context = context;
        _actor = actor;
    }

    public async Task<List<LabelMatch>> ToBoardAsync(IEnumerable<Label> source, Guid boardId)
    {
        ArgumentNullException.ThrowIfNull(source);

        var onBoard = await _context.Labels.Where(label => label.BoardId == boardId).ToListAsync();
        var byKey = new Dictionary<(string Name, Color? Color), Label>();
        foreach (var label in onBoard)
        {
            byKey.TryAdd(MatchKey(label), label);
        }

        var matches = new List<LabelMatch>();
        foreach (var label in source)
        {
            var key = MatchKey(label);
            if (byKey.TryGetValue(key, out var match))
            {
                matches.Add(new LabelMatch(label, match, false));
                continue;
            }

            match = CopyOnto(label, boardId);
            _context.Labels.Add(match);
            byKey[key] = match;
            matches.Add(new LabelMatch(label, match, true));
        }

        return matches;
    }

    private Label CopyOnto(Label label, Guid boardId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = label.Name,
            BoardId = boardId,
            Color = label.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actor.Id
        };

    private static (string Name, Color? Color) MatchKey(Label label) =>
        label.Name.Trim() is { Length: > 0 } name
            ? (name.ToLowerInvariant(), null)
            : (string.Empty, label.Color);
}
