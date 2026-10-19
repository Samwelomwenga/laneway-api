using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface ICommentService
{
    Task<ApiResponse<ActivityEntryDto>> CreateAsync(Guid cardId, CommentTextWrite write);
    Task<ApiResponse<ActivityEntryDto>> UpdateAsync(Guid id, CommentTextWrite write);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class CommentService : ICommentService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly ArchiveGuard _archive;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;
    private readonly Readable _readable;

    public CommentService(
        ApplicationDbContext context,
        Actor actor,
        ArchiveGuard archive,
        ActivityWriter activity,
        ActivityTree tree,
        Readable readable)
    {
        _context = context;
        _actor = actor;
        _archive = archive;
        _activity = activity;
        _tree = tree;
        _readable = readable;
    }

    public async Task<ApiResponse<ActivityEntryDto>> CreateAsync(Guid cardId, CommentTextWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var access = await _readable.CardAsync(cardId, _actor.Id);
        if (_readable.Denies(access, BoardAction.Comment, AccessTarget.Card, cardId, _actor.Id, out ApiResponse<ActivityEntryDto>? denied))
        {
            return denied;
        }

        if (!await _context.TryLockCardAsync(cardId))
        {
            return CardNotFound<ActivityEntryDto>();
        }

        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<ActivityEntryDto>(archived, TreeItem.Comment);
        }

        var chain = await _tree.CardAsync(cardId);
        var entry = await _activity.AddAsync(
            ActivityType.Comment,
            chain.Place,
            actor => new CommentData(actor, chain.Workspace, chain.Board, chain.List, chain.Card));
        entry.Text = write.Text;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<ActivityEntryDto>.SuccessResponse(
            ActivityView.Of(entry), "Comment created successfully", 201);
    }

    public async Task<ApiResponse<ActivityEntryDto>> UpdateAsync(Guid id, CommentTextWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var found = await FindAsync<ActivityEntryDto>(id, adminMayDelete: false);
        if (found.Refused is { } refused)
        {
            return refused;
        }

        var comment = found.Comment!;
        if (comment.Text != write.Text)
        {
            comment.Text = write.Text;
            comment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return ApiResponse<ActivityEntryDto>.SuccessResponse(
            ActivityView.Of(comment), "Comment updated successfully");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var found = await FindAsync<bool>(id, adminMayDelete: true);
        if (found.Refused is { } refused)
        {
            return refused;
        }

        _context.ActivityEntries.Remove(found.Comment!);
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Comment deleted successfully", 204);
    }

    private async Task<(ActivityEntry? Comment, ApiResponse<T>? Refused)> FindAsync<T>(Guid id, bool adminMayDelete)
    {
        var userId = _actor.Id;
        var access = await _readable.CommentAsync(id, userId);
        if (_readable.ReadDenies(access, AccessTarget.Comment, id, userId, out ApiResponse<T>? denied))
        {
            return (null, denied);
        }

        var comment = access.Target;
        if (!(adminMayDelete && access.Standing.Role == Role.Admin))
        {
            if (BoardRules.Refusal(access.Standing, BoardAction.Comment) is { } refusal)
            {
                return (null, _readable.Refused<T>(refusal, AccessTarget.Comment, id, userId));
            }

            if (comment.CreatedBy != userId)
            {
                return (null, _readable.Refused<T>(Refusals.NotAuthor, AccessTarget.Comment, id, userId));
            }
        }

        return await _archive.OnCardAsync(comment.CardId!.Value) is { } archived
            ? (null, ArchiveErrors.ReadOnly<T>(archived, TreeItem.Comment))
            : (comment, null);
    }

    private static ApiResponse<T> CardNotFound<T>() => ApiResponse<T>.ErrorResponse("Card not found", 404);
}
