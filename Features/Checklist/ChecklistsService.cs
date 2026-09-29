using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IChecklistService
{
    Task<ApiResponse<ChecklistDto>> CreateAsync(CreateChecklistWrite write);
    Task<ApiResponse<ChecklistDto>> UpdateAsync(Guid id, UpdateChecklistWrite write);
    Task<ApiResponse<ChecklistDto>> ReorderAsync(Guid id, ReorderChecklistDto reorderChecklistDto);
    Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived);
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
    private readonly CardCompletion _completion;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public ChecklistService(
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

    public async Task<ApiResponse<ChecklistDto>> CreateAsync(CreateChecklistWrite write)
    {
        var cardId = write.CardId;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.Cards.AnyAsync(c => c.Id == cardId))
        {
            return ReferenceErrors.NotFound<ChecklistDto>("cardId", "Card", cardId);
        }

        var placed = await _placements.ResolveOnCardAsync(cardId, Placement.Of(write));
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<ChecklistDto>(placed.Errors);
        }

        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<ChecklistDto>(archived, "cardId", TreeItem.Checklist);
        }

        var checklist = MapToEntity(write, _actor.Id, placed.Position);

        _context.Checklists.Add(checklist);
        var chain = await _tree.CardAsync(cardId);
        await _activity.AddAsync(
            ActivityType.CreateChecklist,
            chain.Place,
            actor => new CreateChecklistData(
                actor, chain.Workspace, chain.Board, chain.List, chain.Card, ChecklistRef.Of(checklist)));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        var checklistDto = MapToDto(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist created successfully", 201);
    }

    public async Task<ApiResponse<ChecklistDto>> UpdateAsync(Guid id, UpdateChecklistWrite write)
    {
        var checklist = await WithCheckItemsAsync(id);
        if (checklist == null)
        {
            return ApiResponse<ChecklistDto>.ErrorResponse("Checklist not found", 404);
        }

        if (await _archive.OnChecklistAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<ChecklistDto>(archived, TreeItem.Checklist);
        }

        checklist.Name = write.Name;
        _context.StampChange(checklist, _actor);

        var tracked = _context.Entry(checklist);
        if (tracked.Changed())
        {
            var old = ChecklistFields.Changed(tracked);
            var chain = await _tree.CardAsync(checklist.CardId);
            await _activity.AddAsync(
                ActivityType.UpdateChecklist,
                chain.Place,
                actor => new UpdateChecklistData(
                    actor,
                    chain.Workspace,
                    chain.Board,
                    chain.List,
                    chain.Card,
                    ChecklistRef.Of(checklist),
                    old));
        }

        await _context.SaveChangesAsync();
        var checklistDto = MapToDto(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist updated successfully", 200);
    }

    public async Task<ApiResponse<ChecklistDto>> ReorderAsync(Guid id, ReorderChecklistDto reorderChecklistDto)
    {
        ArgumentNullException.ThrowIfNull(reorderChecklistDto);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var checklist = await WithCheckItemsAsync(id);
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

        var wasAt = checklist.Position;
        checklist.Position = placed.Position;
        _context.StampChange(checklist, _actor);

        if (_context.Entry(checklist).Changed())
        {
            var chain = await _tree.CardAsync(checklist.CardId);
            await _activity.AddAsync(
                ActivityType.MoveChecklist,
                chain.Place,
                actor => new MoveChecklistData(
                    actor,
                    chain.Workspace,
                    chain.Board,
                    chain.List,
                    chain.Card,
                    ChecklistRef.Of(checklist),
                    new PositionChange(wasAt, checklist.Position)));
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        var checklistDto = MapToDto(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist moved successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived)
    {
        ArgumentNullException.ThrowIfNull(archived);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var checklist = await _context.Checklists.FindAsync(id);
        if (checklist == null)
        {
            return ApiResponse<bool>.ErrorResponse("Checklist not found", 404);
        }

        if (await _archive.OnCardAsync(checklist.CardId) is { } holder)
        {
            return ArchiveErrors.RestoreFirst<bool>(holder, TreeItem.Checklist);
        }

        var watch = await _completion.WatchAsync(checklist.CardId);
        await _context.LockChecklistsAsync(id);
        await _context.Entry(checklist).ReloadAsync();

        var response = await _context.SetArchivedAsync(
            checklist, _actor, archived, "Checklist",
            archiving => RecordArchivedAsync(checklist, archiving, watch));
        await transaction.CommitAsync();

        return response;
    }

    private async Task RecordArchivedAsync(Checklist checklist, bool archived, CompletionWatch watch)
    {
        var held = await _completion.ChecklistTallyAsync(checklist.Id);

        if (archived)
        {
            watch.KeepComputedValue(losingCheckItems: held.CheckItemCount);
        }

        var completion = archived ? watch.Losing(held) : watch.Gaining(held);

        var chain = await _tree.CardAsync(checklist.CardId);
        await _activity.AddAsync(
            archived ? ActivityType.ArchiveChecklist : ActivityType.RestoreChecklist,
            chain.Place,
            actor => new ArchiveChecklistData(
                actor,
                chain.Workspace,
                chain.Board,
                chain.List,
                chain.Card,
                ChecklistRef.Of(checklist),
                completion));
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

        var chain = await _tree.CardAsync(checklist.CardId);
        _context.Checklists.Remove(checklist);
        await _activity.AddAsync(
            ActivityType.DeleteChecklist,
            chain.Place,
            actor => new DeleteChecklistData(
                actor, chain.Workspace, chain.Board, chain.List, chain.Card, ChecklistRef.Of(checklist)));
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Checklist deleted successfully", 204);
    }

    public async Task<ApiResponse<ChecklistDto>> GetByIdAsync(Guid id)
    {
        var checklist = await WithCheckItemsAsync(id);
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
            query.InSortOrder().Include(c => c.CheckItems),
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

    private async Task<Checklist?> WithCheckItemsAsync(Guid id) =>
        await _context.Checklists.Include(c => c.CheckItems).FirstOrDefaultAsync(c => c.Id == id);

    private static ChecklistDto MapToDto(Checklist checklist)
    {
        return new ChecklistDto(
            checklist.Id,
            checklist.CardId,
            checklist.Name,
            checklist.Position,
            checklist.IsArchived,
            CheckItemView.Sorted(checklist.CheckItems),
            checklist.CreatedAt,
            checklist.UpdatedAt,
            checklist.CreatedBy,
            checklist.UpdatedBy
        );
    }

    private static Checklist MapToEntity(CreateChecklistWrite write, Guid actorId, double position)
    {
        return new Checklist
        {
            Id = Guid.NewGuid(),
            Name = write.Name,
            Position = position,
            CardId = write.CardId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
