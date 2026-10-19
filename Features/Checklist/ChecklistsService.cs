using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IChecklistService
{
    Task<ApiResponse<ChecklistDto>> CreateAsync(CreateChecklistWrite write);
    Task<ApiResponse<ChecklistDto>> UpdateAsync(Guid id, UpdateChecklistWrite write);
    Task<ApiResponse<ChecklistDto>> ReorderAsync(Guid id, ReorderChecklistDto reorderChecklistDto);
    Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
    Task<ApiResponse<ChecklistDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<List<ChecklistDto>>> GetAllAsync(Guid cardId, ChecklistSearchDto searchDto);
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
    private readonly Readable _readable;

    public ChecklistService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        CardCompletion completion,
        ActivityWriter activity,
        ActivityTree tree,
        Readable readable)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _completion = completion;
        _activity = activity;
        _tree = tree;
        _readable = readable;
    }

    public async Task<ApiResponse<ChecklistDto>> CreateAsync(CreateChecklistWrite write)
    {
        var cardId = write.CardId;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var access = await _readable.CardAsync(cardId, _actor.Id);
        if (_readable.Denies(access, BoardAction.Edit, AccessTarget.Card, cardId, _actor.Id, out ApiResponse<ChecklistDto>? denied))
        {
            return denied;
        }

        var placed = await _placements.ResolveOnCardAsync(cardId, Placement.Of(write), _actor.Id);
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<ChecklistDto>(placed.Errors);
        }

        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<ChecklistDto>(archived, null, TreeItem.Checklist);
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
        var checklistDto = ChecklistView.Of(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist created successfully", 201);
    }

    public async Task<ApiResponse<ChecklistDto>> UpdateAsync(Guid id, UpdateChecklistWrite write)
    {
        var access = await _readable.ChecklistAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Edit, AccessTarget.Checklist, id, _actor.Id, out ApiResponse<ChecklistDto>? denied))
        {
            return denied;
        }

        var checklist = await WithCheckItemsAsync(access.Target);

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
        var checklistDto = ChecklistView.Of(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist updated successfully", 200);
    }

    public async Task<ApiResponse<ChecklistDto>> ReorderAsync(Guid id, ReorderChecklistDto reorderChecklistDto)
    {
        ArgumentNullException.ThrowIfNull(reorderChecklistDto);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var access = await _readable.ChecklistAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Edit, AccessTarget.Checklist, id, _actor.Id, out ApiResponse<ChecklistDto>? denied))
        {
            return denied;
        }

        var checklist = await WithCheckItemsAsync(access.Target);

        if (await _archive.OnChecklistAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<ChecklistDto>(archived, TreeItem.Checklist);
        }

        var placed = await _placements.ResolveReorderOnCardAsync(checklist, Placement.Of(reorderChecklistDto), _actor.Id);
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
        var checklistDto = ChecklistView.Of(checklist);

        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist moved successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived)
    {
        ArgumentNullException.ThrowIfNull(archived);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var access = await _readable.ChecklistAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Edit, AccessTarget.Checklist, id, _actor.Id, out ApiResponse<bool>? denied))
        {
            return denied;
        }

        var checklist = access.Target;

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
        var access = await _readable.ChecklistAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Edit, AccessTarget.Checklist, id, _actor.Id, out ApiResponse<bool>? denied))
        {
            return denied;
        }

        var checklist = access.Target;

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
        var access = await _readable.ChecklistAsync(id, _actor.Id);
        if (_readable.ReadDenies(access, AccessTarget.Checklist, id, _actor.Id, out ApiResponse<ChecklistDto>? denied))
        {
            return denied;
        }

        var checklistDto = ChecklistView.Of(await WithCheckItemsAsync(access.Target));
        return ApiResponse<ChecklistDto>.SuccessResponse(checklistDto, "Checklist retrieved successfully", 200);
    }

    public async Task<ApiResponse<List<ChecklistDto>>> GetAllAsync(Guid cardId, ChecklistSearchDto searchDto)
    {
        ArgumentNullException.ThrowIfNull(searchDto);

        var access = await _readable.CardAsync(cardId, _actor.Id);
        if (_readable.ReadDenies(access, AccessTarget.Card, cardId, _actor.Id, out ApiResponse<List<ChecklistDto>>? denied))
        {
            return denied;
        }

        var query = ArchiveView.Checklists(
            _readable.Checklists(_actor.Id).Where(c => c.CardId == cardId), searchDto.Archived);

        var (checklists, totalCount) = await PagedQuery.ReadAsync(
            query.InSortOrder().Include(c => c.CheckItems),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        return PagedResponse<ChecklistDto>.SuccessResponse(
            checklists.Select(ChecklistView.Of).ToList(),
            totalCount,
            pageSize: searchDto.PageSize,
            pageNumber: searchDto.PageNumber,
            message: "Checklists retrieved successfully"
        );
    }

    private async Task<Checklist> WithCheckItemsAsync(Checklist checklist)
    {
        await _context.Entry(checklist).Collection(c => c.CheckItems).LoadAsync();
        return checklist;
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
