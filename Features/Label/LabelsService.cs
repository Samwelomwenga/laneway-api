using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface ILabelService
{
    Task<ApiResponse<List<LabelDto>>> GetAllAsync(LabelSearchDto searchDto);
    Task<ApiResponse<LabelDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<LabelDto>> CreateAsync(CreateLabelWrite write);
    Task<ApiResponse<LabelDto>> UpdateAsync(Guid id, UpdateLabelWrite write);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class LabelService : ILabelService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly ArchiveGuard _archive;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public LabelService(
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

    public async Task<ApiResponse<List<LabelDto>>> GetAllAsync(LabelSearchDto searchDto)
    {
        if (searchDto.BoardId is { } filterBoardId && !await _context.Boards.AnyAsync(b => b.Id == filterBoardId))
        {
            return ReferenceErrors.NotFound<List<LabelDto>>("boardId", "Board", filterBoardId);
        }

        var query = _context.Labels.AsQueryable();
        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(l => l.Name.ToLower().Contains(term.ToLower()));
        }

        if (searchDto.BoardId is { } boardId)
        {
            query = query.Where(l => l.BoardId == boardId);
        }

        var (labels, totalCount) = await PagedQuery.ReadAsync(
            query.OrderBy(l => l.Name).ThenBy(l => l.Color).ThenBy(l => l.CreatedAt).ThenBy(l => l.Id),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        var labelDtos = labels.Select(MapToDto).ToList();

        return PagedResponse<LabelDto>.SuccessResponse(
            labelDtos,
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Labels retrieved successfully"
        );
    }

    public async Task<ApiResponse<LabelDto>> GetByIdAsync(Guid id)
    {
        var label = await _context.Labels.FindAsync(id);
        if (label == null)
        {
            return ApiResponse<LabelDto>.ErrorResponse("Label not found", 404);
        }
        var labelDto = MapToDto(label);
        return ApiResponse<LabelDto>.SuccessResponse(labelDto, "Label retrieved successfully", 200);
    }

    public async Task<ApiResponse<LabelDto>> CreateAsync(CreateLabelWrite write)
    {
        var boardId = write.BoardId;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.TryLockBoardAsync(boardId))
        {
            return ReferenceErrors.NotFound<LabelDto>("boardId", "Board", boardId);
        }

        if (await _archive.OnBoardAsync(boardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<LabelDto>(archived, "boardId", TreeItem.Label);
        }

        if (await FindDuplicateAsync(boardId, write.Name, write.Color, self: null) is { } duplicate)
        {
            return duplicate;
        }

        var newLabel = MapToEntity(write, _actor.Id);
        _context.Labels.Add(newLabel);
        var chain = await _tree.BoardAsync(boardId);
        await _activity.AddAsync(
            ActivityType.CreateLabel,
            chain.Place,
            actor => new CreateLabelData(actor, chain.Workspace, chain.Board, LabelRef.Of(newLabel)));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var labelDto = MapToDto(newLabel);
        return ApiResponse<LabelDto>.SuccessResponse(labelDto, "Label created successfully", 201);
    }

    public async Task<ApiResponse<LabelDto>> UpdateAsync(Guid id, UpdateLabelWrite write)
    {
        var existingLabel = await _context.Labels.FindAsync(id);
        if (existingLabel == null)
        {
            return ApiResponse<LabelDto>.ErrorResponse("Label not found", 404);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.TryLockBoardAsync(existingLabel.BoardId);

        if (await _archive.OnBoardAsync(existingLabel.BoardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<LabelDto>(archived, TreeItem.Label);
        }

        if (await FindDuplicateAsync(existingLabel.BoardId, write.Name, write.Color, id) is { } duplicate)
        {
            return duplicate;
        }

        existingLabel.Name = write.Name;
        existingLabel.Color = write.Color;
        _context.StampChange(existingLabel, _actor);

        var tracked = _context.Entry(existingLabel);
        if (tracked.Changed())
        {
            var old = LabelFields.Changed(tracked);
            var chain = await _tree.BoardAsync(existingLabel.BoardId);
            await _activity.AddAsync(
                ActivityType.UpdateLabel,
                chain.Place,
                actor => new UpdateLabelData(
                    actor, chain.Workspace, chain.Board, LabelRef.Of(existingLabel), old));
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var labelDto = MapToDto(existingLabel);
        return ApiResponse<LabelDto>.SuccessResponse(labelDto, "Label updated successfully", 200);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var existingLabel = await _context.Labels.FindAsync(id);
        if (existingLabel == null)
        {
            return ApiResponse<bool>.ErrorResponse("Label not found", 404);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.TryLockBoardAsync(existingLabel.BoardId);

        if (await _archive.OnBoardAsync(existingLabel.BoardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.Label);
        }

        var chain = await _tree.BoardAsync(existingLabel.BoardId);
        _context.Labels.Remove(existingLabel);
        await _activity.AddAsync(
            ActivityType.DeleteLabel,
            chain.Place,
            actor => new DeleteLabelData(actor, chain.Workspace, chain.Board, LabelRef.Of(existingLabel)));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Label deleted successfully", 204);
    }

    private async Task<ApiResponse<LabelDto>?> FindDuplicateAsync(Guid boardId, string name, Color? color, Guid? self)
    {
        var others = _context.Labels.Where(l => l.BoardId == boardId);
        if (self is { } id)
        {
            others = others.Where(l => l.Id != id);
        }

        if (name.Length > 0)
        {
            return await others.AnyAsync(l => l.Name.ToLower() == name.ToLower())
                ? ApiResponse<LabelDto>.ErrorResponse("Label with the same name already exists", 409,
                    [new ApiError("name", ErrorCodes.Duplicate, "This board already has a label with this name.")])
                : null;
        }

        return await others.AnyAsync(l => l.Name == string.Empty && l.Color == color)
            ? ApiResponse<LabelDto>.ErrorResponse("Label with the same color already exists", 409,
                [new ApiError("color", ErrorCodes.Duplicate, "This board already has an unnamed label in this color.")])
            : null;
    }

    private static LabelDto MapToDto(Label label)
    {
        return new LabelDto
        (
            label.Id,
            label.Name,
            label.BoardId,
            label.Color,
            label.CreatedAt,
            label.UpdatedAt,
            label.CreatedBy,
            label.UpdatedBy
        );
    }

    private static Label MapToEntity(CreateLabelWrite write, Guid actorId)
    {
        return new Label
        {
            Id = Guid.NewGuid(),
            Name = write.Name,
            BoardId = write.BoardId,
            Color = write.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
