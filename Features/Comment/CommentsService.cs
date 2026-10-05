using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICommentService
{
    Task<ApiResponse<ActivityEntryDto>> CreateAsync(Guid cardId, CommentTextDto commentTextDto);
    Task<ApiResponse<ActivityEntryDto>> UpdateAsync(Guid cardId, Guid id, CommentTextDto commentTextDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid cardId, Guid id);
}

public class CommentService : ICommentService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly ArchiveGuard _archive;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public CommentService(
        ApplicationDbContext context,
        Actor actor,
        ArchiveGuard archive,
        ActivityWriter activity,
        ActivityTree tree)
    {
        _context = context;
        _actor = actor;
        _archive = archive;
        _activity = activity;
        _tree = tree;
    }

    public async Task<ApiResponse<ActivityEntryDto>> CreateAsync(Guid cardId, CommentTextDto commentTextDto)
    {
        ArgumentNullException.ThrowIfNull(commentTextDto);

        await using var transaction = await _context.Database.BeginTransactionAsync();

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
        entry.Text = commentTextDto.Text;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<ActivityEntryDto>.SuccessResponse(
            ActivityView.Of(entry), "Comment created successfully", 201);
    }

    public async Task<ApiResponse<ActivityEntryDto>> UpdateAsync(
        Guid cardId, Guid id, CommentTextDto commentTextDto)
    {
        ArgumentNullException.ThrowIfNull(commentTextDto);

        var found = await FindAsync<ActivityEntryDto>(cardId, id);
        if (found.Refused is { } refused)
        {
            return refused;
        }

        var comment = found.Comment!;
        if (comment.Text != commentTextDto.Text)
        {
            comment.Text = commentTextDto.Text;
            comment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return ApiResponse<ActivityEntryDto>.SuccessResponse(
            ActivityView.Of(comment), "Comment updated successfully");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid cardId, Guid id)
    {
        var found = await FindAsync<bool>(cardId, id);
        if (found.Refused is { } refused)
        {
            return refused;
        }

        _context.ActivityEntries.Remove(found.Comment!);
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Comment deleted successfully", 204);
    }

    private async Task<(ActivityEntry? Comment, ApiResponse<T>? Refused)> FindAsync<T>(Guid cardId, Guid id)
    {
        if (!await _context.Cards.AnyAsync(card => card.Id == cardId))
        {
            return (null, CardNotFound<T>());
        }

        var comment = await _context.ActivityEntries.FirstOrDefaultAsync(entry =>
            entry.Id == id && entry.CardId == cardId && entry.Type == ActivityType.Comment);
        if (comment is null)
        {
            return (null, NotFound<T>());
        }

        if (comment.CreatedBy != _actor.Id)
        {
            return (null, NotAuthor<T>());
        }

        return await _archive.OnCardAsync(cardId) is { } archived
            ? (null, ArchiveErrors.ReadOnly<T>(archived, TreeItem.Comment))
            : (comment, null);
    }

    private static ApiResponse<T> CardNotFound<T>() => ApiResponse<T>.ErrorResponse("Card not found", 404);

    private static ApiResponse<T> NotFound<T>() => ApiResponse<T>.ErrorResponse("Comment not found", 404);

    private static ApiResponse<T> NotAuthor<T>() =>
        ApiResponse<T>.ErrorResponse("You didn't write this comment", 403,
            [new ApiError(null, ErrorCodes.NotAuthor, "Only the author can edit or delete a comment.")]);
}
