using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICheckItemService
{
    Task<ApiResponse<CheckItemDto>> CreateAsync(CreateCheckItemWrite write);
    Task<ApiResponse<CheckItemDto>> UpdateAsync(Guid id, UpdateCheckItemWrite write);
    Task<ApiResponse<CheckItemDto>> MoveAsync(Guid id, MoveCheckItemWrite write);
    Task<ApiResponse<bool>> SetCheckedAsync(Guid id, CheckedWrite write);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
    Task<ApiResponse<CheckItemDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<List<CheckItemDto>>> GetAllAsync(CheckItemSearchDto searchDto);
}

public class CheckItemService : ICheckItemService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;
    private readonly ArchiveGuard _archive;
    private readonly CardCompletion _completion;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public CheckItemService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        CardCompletion completion,
        ActivityWriter activity,
        ActivityTree tree)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _completion = completion;
        _activity = activity;
        _tree = tree;
    }

    public async Task<ApiResponse<CheckItemDto>> CreateAsync(CreateCheckItemWrite write)
    {
        var checklistId = write.ChecklistId;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (await CardOfChecklistAsync(checklistId) is not { } cardId)
        {
            return ReferenceErrors.NotFound<CheckItemDto>("checklistId", "Checklist", checklistId);
        }

        var watch = await _completion.WatchAsync(cardId);
        var placed = await _placements.ResolveInChecklistAsync(checklistId, Placement.Of(write));
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CheckItemDto>(placed.Errors);
        }

        if (await _archive.OnChecklistAsync(checklistId) is { } archived)
        {
            return ArchiveErrors.NoCreate<CheckItemDto>(archived, "checklistId", TreeItem.CheckItem);
        }

        if (await IsFullAsync(checklistId))
        {
            return Full<CheckItemDto>();
        }

        var checkItem = MapToEntity(write, _actor.Id, placed.Position);

        _context.CheckItems.Add(checkItem);
        var completion = watch.Change(
            gainedCheckItems: 1, gainedCheckedItems: checkItem.IsChecked ? 1 : 0);
        var chain = await _tree.ChecklistAsync(checklistId);
        await _activity.AddAsync(
            ActivityType.CreateCheckItem,
            chain.Place,
            actor => new CreateCheckItemData(
                actor,
                chain.Workspace,
                chain.Board,
                chain.List,
                chain.Card,
                chain.Checklist,
                CheckItemRef.Of(checkItem),
                completion));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<CheckItemDto>.SuccessResponse(
            CheckItemView.Of(checkItem), "Check item created successfully", 201);
    }

    public async Task<ApiResponse<CheckItemDto>> UpdateAsync(Guid id, UpdateCheckItemWrite write)
    {
        var checkItem = await _context.CheckItems.FindAsync(id);
        if (checkItem == null)
        {
            return ApiResponse<CheckItemDto>.ErrorResponse("Check item not found", 404);
        }

        if (await _archive.OnChecklistAsync(checkItem.ChecklistId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<CheckItemDto>(archived, TreeItem.CheckItem);
        }

        checkItem.Name = write.Name;
        _context.StampChange(checkItem, _actor);

        var tracked = _context.Entry(checkItem);
        if (tracked.Changed())
        {
            var old = CheckItemFields.Changed(tracked);
            var chain = await _tree.ChecklistAsync(checkItem.ChecklistId);
            await _activity.AddAsync(
                ActivityType.UpdateCheckItem,
                chain.Place,
                actor => new UpdateCheckItemData(
                    actor,
                    chain.Workspace,
                    chain.Board,
                    chain.List,
                    chain.Card,
                    chain.Checklist,
                    CheckItemRef.Of(checkItem),
                    old));
        }

        await _context.SaveChangesAsync();

        return ApiResponse<CheckItemDto>.SuccessResponse(
            CheckItemView.Of(checkItem), "Check item updated successfully", 200);
    }

    public async Task<ApiResponse<CheckItemDto>> MoveAsync(Guid id, MoveCheckItemWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var checklistId = write.ChecklistId;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var checkItem = await _context.CheckItems.FindAsync(id);
        if (checkItem == null)
        {
            return ApiResponse<CheckItemDto>.ErrorResponse("Check item not found", 404);
        }

        if (await _archive.OnChecklistAsync(checkItem.ChecklistId) is { } readOnly)
        {
            return ArchiveErrors.ReadOnly<CheckItemDto>(readOnly, TreeItem.CheckItem);
        }

        var cardId = await CardOfChecklistAsync(checkItem.ChecklistId);
        var destinationCardId = await CardOfChecklistAsync(checklistId);

        var errors = new List<ApiError>();
        if (destinationCardId is null)
        {
            errors.Add(new ApiError("checklistId", ErrorCodes.NotFound, $"Checklist {checklistId} does not exist."));
        }
        else if (destinationCardId != cardId)
        {
            errors.Add(new ApiError("checklistId", ErrorCodes.NotOnCard,
                $"Checklist {checklistId} isn't on card {cardId}."));
        }

        var position = checkItem.Position;
        if (destinationCardId is not null)
        {
            var placed = await _placements.ResolveMoveInChecklistAsync(
                checkItem, checklistId, Placement.Of(write));
            errors.AddRange(placed.Errors);
            position = placed.Position;
        }

        if (errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CheckItemDto>(errors);
        }

        if (await _archive.OnChecklistAsync(checklistId) is { } archived)
        {
            return ArchiveErrors.NoCreate<CheckItemDto>(archived, "checklistId", TreeItem.CheckItem);
        }

        if (checklistId != checkItem.ChecklistId && await IsFullAsync(checklistId))
        {
            return Full<CheckItemDto>();
        }

        var leaving = checkItem.ChecklistId;
        var wasAt = checkItem.Position;
        checkItem.ChecklistId = checklistId;
        checkItem.Position = position;
        _context.StampChange(checkItem, _actor);

        if (_context.Entry(checkItem).Changed())
        {
            var chain = await _tree.ChecklistAsync(checklistId);
            var from = leaving == checklistId
                ? null
                : new CheckItemOrigin(await _tree.ChecklistRefAsync(leaving));
            await _activity.AddAsync(
                ActivityType.MoveCheckItem,
                chain.Place,
                actor => new MoveCheckItemData(
                    actor,
                    chain.Workspace,
                    chain.Board,
                    chain.List,
                    chain.Card,
                    chain.Checklist,
                    CheckItemRef.Of(checkItem),
                    new PositionChange(wasAt, checkItem.Position),
                    from));
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<CheckItemDto>.SuccessResponse(
            CheckItemView.Of(checkItem), "Check item moved successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetCheckedAsync(Guid id, CheckedWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var checkItem = await _context.CheckItems.FindAsync(id);
        if (checkItem == null)
        {
            return ApiResponse<bool>.ErrorResponse("Check item not found", 404);
        }

        if (await _archive.OnChecklistAsync(checkItem.ChecklistId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.CheckItem);
        }

        var watch = await WatchCardAsync(checkItem);
        var value = write.Value;
        if (checkItem.IsChecked != value)
        {
            checkItem.IsChecked = value;
            _context.StampChange(checkItem, _actor);

            var completion = watch.Change(gainedCheckedItems: value ? 1 : -1);
            var chain = await _tree.ChecklistAsync(checkItem.ChecklistId);
            await _activity.AddAsync(
                value ? ActivityType.CheckCheckItem : ActivityType.UncheckCheckItem,
                chain.Place,
                actor => new CheckedCheckItemData(
                    actor,
                    chain.Workspace,
                    chain.Board,
                    chain.List,
                    chain.Card,
                    chain.Checklist,
                    CheckItemRef.Of(checkItem),
                    completion));
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        return ApiResponse<bool>.SuccessResponse(
            true, $"Check item {(value ? "checked" : "unchecked")}", 204);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var checkItem = await _context.CheckItems.FindAsync(id);
        if (checkItem == null)
        {
            return ApiResponse<bool>.ErrorResponse("Check item not found", 404);
        }

        if (await _archive.OnChecklistAsync(checkItem.ChecklistId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.CheckItem);
        }

        var watch = await WatchCardAsync(checkItem);
        watch.KeepComputedValue(losingCheckItems: 1);
        var completion = watch.Change(
            gainedCheckItems: -1, gainedCheckedItems: checkItem.IsChecked ? -1 : 0);

        var chain = await _tree.ChecklistAsync(checkItem.ChecklistId);
        _context.CheckItems.Remove(checkItem);
        await _activity.AddAsync(
            ActivityType.DeleteCheckItem,
            chain.Place,
            actor => new DeleteCheckItemData(
                actor,
                chain.Workspace,
                chain.Board,
                chain.List,
                chain.Card,
                chain.Checklist,
                CheckItemRef.Of(checkItem),
                completion));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Check item deleted successfully", 204);
    }

    public async Task<ApiResponse<CheckItemDto>> GetByIdAsync(Guid id)
    {
        var checkItem = await _context.CheckItems.FindAsync(id);
        if (checkItem == null)
        {
            return ApiResponse<CheckItemDto>.ErrorResponse("Check item not found", 404);
        }

        return ApiResponse<CheckItemDto>.SuccessResponse(
            CheckItemView.Of(checkItem), "Check item retrieved successfully", 200);
    }

    public async Task<ApiResponse<List<CheckItemDto>>> GetAllAsync(CheckItemSearchDto searchDto)
    {
        ArgumentNullException.ThrowIfNull(searchDto);

        if (searchDto.ChecklistId is { } filterChecklistId
            && !await _context.Checklists.AnyAsync(c => c.Id == filterChecklistId))
        {
            return ReferenceErrors.NotFound<List<CheckItemDto>>("checklistId", "Checklist", filterChecklistId);
        }

        var query = _context.CheckItems.AsQueryable();

        if (searchDto.ChecklistId is { } checklistId)
        {
            query = query.Where(checkItem => checkItem.ChecklistId == checklistId);
        }

        query = ArchiveView.CheckItems(query, searchDto.Archived, _context);

        var (checkItems, totalCount) = await PagedQuery.ReadAsync(
            query.InSortOrder(),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        return PagedResponse<CheckItemDto>.SuccessResponse(
            checkItems.ConvertAll(CheckItemView.Of),
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Check items retrieved successfully"
        );
    }

    private async Task<CompletionWatch> WatchCardAsync(CheckItem checkItem)
    {
        var watch = await _completion.WatchOnChecklistAsync(checkItem.ChecklistId);
        await _context.LockChecklistsAsync(checkItem.ChecklistId);
        await _context.Entry(checkItem).ReloadAsync();
        return watch;
    }

    private async Task<Guid?> CardOfChecklistAsync(Guid checklistId) =>
        await _context.Checklists
            .Where(checklist => checklist.Id == checklistId)
            .Select(checklist => (Guid?)checklist.CardId)
            .FirstOrDefaultAsync();

    private async Task<bool> IsFullAsync(Guid checklistId) =>
        await _context.CheckItems.CountAsync(checkItem => checkItem.ChecklistId == checklistId)
        >= FieldLimits.CheckItemsPerChecklist;

    private static ApiResponse<T> Full<T>() =>
        ApiResponse<T>.ErrorResponse("The checklist is full", 409,
        [
            new ApiError("checklistId", ErrorCodes.LimitReached,
                $"A checklist holds at most {FieldLimits.CheckItemsPerChecklist} check items.")
        ]);

    private static CheckItem MapToEntity(CreateCheckItemWrite write, Guid actorId, double position)
    {
        return new CheckItem
        {
            Id = Guid.NewGuid(),
            Name = write.Name,
            Position = position,
            ChecklistId = write.ChecklistId,
            IsChecked = write.IsChecked,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
