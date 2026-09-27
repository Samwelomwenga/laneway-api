using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed class ActivityWriter
{
    private static readonly JsonSerializerOptions DataOptions = BuildDataOptions();

    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;

    private ActorRef? _actorRef;

    public ActivityWriter(ApplicationDbContext context, Actor actor)
    {
        _context = context;
        _actor = actor;
    }

    public async Task<ActivityEntry> AddAsync<TData>(
        ActivityType type, ActivityPlace place, Func<ActorRef, TData> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var entry = new ActivityEntry
        {
            Id = Guid.NewGuid(),
            Type = type,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actor.Id,
            WorkspaceId = place.WorkspaceId,
            BoardId = place.BoardId,
            ListId = place.ListId,
            CardId = place.CardId,
            FromWorkspaceId = place.FromWorkspaceId,
            FromBoardId = place.FromBoardId,
            FromListId = place.FromListId,
            Data = JsonSerializer.SerializeToDocument(data(await ActorAsync()), DataOptions)
        };

        _context.ActivityEntries.Add(entry);
        return entry;
    }

    private static JsonSerializerOptions BuildDataOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        JsonSettings.Apply(options);
        options.Converters.Add(new WasConverter());
        return options;
    }

    private async Task<ActorRef> ActorAsync() =>
        _actorRef ??= await _context.Users
            .Where(user => user.Id == _actor.Id)
            .Select(user => new ActorRef(user.Id, user.Username, user.FirstName, user.LastName))
            .FirstAsync();
}
