using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICheckItemService
{
    Task<ApiResponse<CheckItemDto>> CreateAsync(CreateCheckItemDto createCheckItemDto);
    Task<ApiResponse<CheckItemDto>> UpdateAsync(Guid id, UpdateCheckItemDto updateCheckItemDto);
    Task<ApiResponse<CheckItemDto>> MoveAsync(Guid id, MoveCheckItemDto moveCheckItemDto);
    Task<ApiResponse<bool>> SetCheckedAsync(Guid id, CheckedDto checkedDto);
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

    public CheckItemService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        CardCompletion completion)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _completion = completion;
    }

    public async Task<ApiResponse<CheckItemDto>> CreateAsync(CreateCheckItemDto createCheckItemDto)
    {
        var checklistId = createCheckItemDto.ChecklistId!.Value;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.Checklists.AnyAsync(c => c.Id == checklistId))
        {
            return ReferenceErrors.NotFound<CheckItemDto>("checklistId", "Checklist", checklistId);
        }

        var placed = await _placements.ResolveInChecklistAsync(checklistId, Placement.Of(createCheckItemDto));
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

        var checkItem = MapToEntity(createCheckItemDto, _actor.Id, placed.Position);

        _context.CheckItems.Add(checkItem);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<CheckItemDto>.SuccessResponse(
            CheckItemView.Of(checkItem), "Check item created successfully", 201);
    }

    public async Task<ApiResponse<CheckItemDto>> UpdateAsync(Guid id, UpdateCheckItemDto updateCheckItemDto)
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

        checkItem.Name = updateCheckItemDto.Name!;
        _context.StampChange(checkItem, _actor);

        await _context.SaveChangesAsync();

        return ApiResponse<CheckItemDto>.SuccessResponse(
            CheckItemView.Of(checkItem), "Check item updated successfully", 200);
    }

    public async Task<ApiResponse<CheckItemDto>> MoveAsync(Guid id, MoveCheckItemDto moveCheckItemDto)
    {
        ArgumentNullException.ThrowIfNull(moveCheckItemDto);

        var checklistId = moveCheckItemDto.ChecklistId!.Value;
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
                checkItem, checklistId, Placement.Of(moveCheckItemDto));
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

        checkItem.ChecklistId = checklistId;
        checkItem.Position = position;
        _context.StampChange(checkItem, _actor);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<CheckItemDto>.SuccessResponse(
            CheckItemView.Of(checkItem), "Check item moved successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetCheckedAsync(Guid id, CheckedDto checkedDto)
    {
        ArgumentNullException.ThrowIfNull(checkedDto);

        var checkItem = await _context.CheckItems.FindAsync(id);
        if (checkItem == null)
        {
            return ApiResponse<bool>.ErrorResponse("Check item not found", 404);
        }

        if (await _archive.OnChecklistAsync(checkItem.ChecklistId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.CheckItem);
        }

        var value = checkedDto.Value!.Value;
        if (checkItem.IsChecked != value)
        {
            checkItem.IsChecked = value;
            _context.StampChange(checkItem, _actor);
            await _context.SaveChangesAsync();
        }

        return ApiResponse<bool>.SuccessResponse(
            true, $"Check item {(value ? "checked" : "unchecked")}", 204);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var checkItem = await _context.CheckItems.FindAsync(id);
        if (checkItem == null)
        {
            return ApiResponse<bool>.ErrorResponse("Check item not found", 404);
        }

        if (await _archive.OnChecklistAsync(checkItem.ChecklistId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.CheckItem);
        }

        if (await CardOfChecklistAsync(checkItem.ChecklistId) is { } cardId)
        {
            await _completion.KeepComputedValueAsync(cardId, losingCheckItems: 1);
        }

        _context.CheckItems.Remove(checkItem);
        await _context.SaveChangesAsync();

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

    private static CheckItem MapToEntity(CreateCheckItemDto createCheckItemDto, Guid actorId, double position)
    {
        return new CheckItem
        {
            Id = Guid.NewGuid(),
            Name = createCheckItemDto.Name!,
            Position = position,
            ChecklistId = createCheckItemDto.ChecklistId!.Value,
            IsChecked = createCheckItemDto.IsChecked ?? false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
