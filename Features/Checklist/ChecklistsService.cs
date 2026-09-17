using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IChecklistService
{
    Task<ApiResponse<ChecklistDto>> CreateAsync(CreateChecklistDto createChecklistDto);
    Task<ApiResponse<ChecklistDto>> UpdateAsync(Guid id, UpdateChecklistDto updateChecklistDto);
    Task<ApiResponse<ChecklistDto>> ReorderAsync(Guid id, ReorderChecklistDto reorderChecklistDto);
    Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedDto archivedDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
    Task<ApiResponse<ChecklistDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<List<ChecklistDto>>> GetAllAsync(ChecklistSearchDto searchDto);
}

public class ChecklistService : IChecklistService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;
    private readonly ArchiveGuard _archive;

    public ChecklistService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
    }

    public async Task<ApiResponse<ChecklistDto>> CreateAsync(CreateChecklistDto createChecklistDto)
    {
        var cardId = createChecklistDto.CardId!.Value;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.Cards.AnyAsync(c => c.Id == cardId))
        {
            return ReferenceErrors.NotFound<ChecklistDto>("cardId", "Card", cardId);
        }

        var placed = await _placements.ResolveOnCardAsync(cardId, Placement.Of(createChecklistDto));
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<ChecklistDto>(placed.Errors);
        }

        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<ChecklistDto>(archived, "cardId", TreeItem.Checklist);
        }

        var checklist = MapToEntity(createChecklistDto, _actor.Id, placed.Position);

        _context.Checklists.Add(checklist);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        var checklistDto = MapToDto(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist created successfully", 201);
    }

    public async Task<ApiResponse<ChecklistDto>> UpdateAsync(Guid id, UpdateChecklistDto updateChecklistDto)
    {
        var checklist = await _context.Checklists.FindAsync(id);
        if (checklist == null)
        {
            return ApiResponse<ChecklistDto>.ErrorResponse("Checklist not found", 404);
        }

        if (await _archive.OnChecklistAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<ChecklistDto>(archived, TreeItem.Checklist);
        }

        checklist.Name = updateChecklistDto.Name!;
        _context.StampChange(checklist, _actor);

        await _context.SaveChangesAsync();
        var checklistDto = MapToDto(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist updated successfully", 200);
    }

    public async Task<ApiResponse<ChecklistDto>> ReorderAsync(Guid id, ReorderChecklistDto reorderChecklistDto)
    {
        ArgumentNullException.ThrowIfNull(reorderChecklistDto);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var checklist = await _context.Checklists.FindAsync(id);
        if (checklist == null)
        {
            return ApiResponse<ChecklistDto>.ErrorResponse("Checklist not found", 404);
        }

        if (await _archive.OnChecklistAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<ChecklistDto>(archived, TreeItem.Checklist);
        }

        var placed = await _placements.ResolveReorderOnCardAsync(checklist, Placement.Of(reorderChecklistDto));
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<ChecklistDto>(placed.Errors);
        }

        checklist.Position = placed.Position;
        _context.StampChange(checklist, _actor);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        var checklistDto = MapToDto(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist moved successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedDto archivedDto)
    {
        var checklist = await _context.Checklists.FindAsync(id);
        if (checklist == null)
        {
            return ApiResponse<bool>.ErrorResponse("Checklist not found", 404);
        }

        if (await _archive.OnCardAsync(checklist.CardId) is { } archived)
        {
            return ArchiveErrors.RestoreFirst<bool>(archived, TreeItem.Checklist);
        }

        return await _context.SetArchivedAsync(checklist, _actor, archivedDto, "Checklist");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var checklist = await _context.Checklists.FindAsync(id);
        if (checklist == null)
        {
            return ApiResponse<bool>.ErrorResponse("Checklist not found", 404);
        }

        if (await _archive.OnCardAsync(checklist.CardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.Checklist);
        }

        if (!checklist.IsArchived)
        {
            return ArchiveErrors.NotArchived<bool>(TreeItem.Checklist);
        }

        _context.Checklists.Remove(checklist);
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Checklist deleted successfully", 204);
    }

    public async Task<ApiResponse<ChecklistDto>> GetByIdAsync(Guid id)
    {
        var checklist = await _context.Checklists.FindAsync(id);
        if (checklist == null)
        {
            return ApiResponse<ChecklistDto>.ErrorResponse("Checklist not found", 404);
        }

        var checklistDto = MapToDto(checklist);
        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist retrieved successfully", 200);
    }

    public async Task<ApiResponse<List<ChecklistDto>>> GetAllAsync(ChecklistSearchDto searchDto)
    {
        ArgumentNullException.ThrowIfNull(searchDto);

        if (searchDto.CardId is { } filterCardId && !await _context.Cards.AnyAsync(c => c.Id == filterCardId))
        {
            return ReferenceErrors.NotFound<List<ChecklistDto>>("cardId", "Card", filterCardId);
        }

        var query = _context.Checklists.AsQueryable();

        if (searchDto.CardId is { } cardId)
        {
            query = query.Where(c => c.CardId == cardId);
        }

        query = ArchiveView.Checklists(query, searchDto.Archived, _context);

        var (checklists, totalCount) = await PagedQuery.ReadAsync(
            query.InSortOrder(),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        return PagedResponse<ChecklistDto>.SuccessResponse(
            checklists.Select(MapToDto).ToList(),
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Checklists retrieved successfully"
        );
    }

    private static ChecklistDto MapToDto(Checklist checklist)
    {
        return new ChecklistDto(
            checklist.Id,
            checklist.CardId,
            checklist.Name,
            checklist.Position,
            checklist.IsArchived,
            [],
            checklist.CreatedAt,
            checklist.UpdatedAt,
            checklist.CreatedBy,
            checklist.UpdatedBy
        );
    }

    private static Checklist MapToEntity(CreateChecklistDto createChecklistDto, Guid actorId, double position)
    {
        return new Checklist
        {
            Id = Guid.NewGuid(),
            Name = createChecklistDto.Name!,
            Position = position,
            CardId = createChecklistDto.CardId!.Value,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
