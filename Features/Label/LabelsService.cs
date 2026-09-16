using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ILabelService
{
    Task<ApiResponse<List<LabelDto>>> GetAllAsync(LabelSearchDto searchDto);
    Task<ApiResponse<LabelDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<LabelDto>> CreateAsync(CreateLabelDto createLabelDto);
    Task<ApiResponse<LabelDto>> UpdateAsync(Guid id, UpdateLabelDto updateLabelDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class LabelService : ILabelService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;

    public LabelService(ApplicationDbContext context, Actor actor)
    {
        _context = context;
        _actor = actor;
    }

    public async Task<ApiResponse<List<LabelDto>>> GetAllAsync(LabelSearchDto searchDto)
    {
        var query = _context.Labels.AsQueryable();
        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(l => l.Name.ToLower().Contains(term.ToLower()));
        }

        var (labels, totalCount) = await PagedQuery.ReadAsync(
            query.OrderBy(l => l.Name),
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

    public async Task<ApiResponse<LabelDto>> CreateAsync(CreateLabelDto createLabelDto)
    {
        var existingLabel = await _context.Labels
            .FirstOrDefaultAsync(l => l.Name.ToLower() == createLabelDto.Name!.ToLower());
        if (existingLabel != null)
        {
            return ApiResponse<LabelDto>.ErrorResponse("Label with the same name already exists", 409,
                [new ApiError("name", ErrorCodes.Duplicate, "A label with this name already exists.")]);
        }
        var newLabel = MapToEntity(createLabelDto, _actor.Id);
        _context.Labels.Add(newLabel);
        await _context.SaveChangesAsync();
        var labelDto = MapToDto(newLabel);
        return ApiResponse<LabelDto>.SuccessResponse(labelDto, "Label created successfully", 201);
    }

    public async Task<ApiResponse<LabelDto>> UpdateAsync(Guid id, UpdateLabelDto updateLabelDto)
    {
        var existingLabel = await _context.Labels.FindAsync(id);
        if (existingLabel == null)
        {
            return ApiResponse<LabelDto>.ErrorResponse("Label not found", 404);
        }

        var nameTaken = await _context.Labels
            .AnyAsync(l => l.Id != id && l.Name.ToLower() == updateLabelDto.Name!.ToLower());
        if (nameTaken)
        {
            return ApiResponse<LabelDto>.ErrorResponse("Label with the same name already exists", 409,
                [new ApiError("name", ErrorCodes.Duplicate, "A label with this name already exists.")]);
        }

        existingLabel.Name = updateLabelDto.Name!;
        existingLabel.Color = updateLabelDto.Color;
        _context.StampChange(existingLabel, _actor);

        await _context.SaveChangesAsync();

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

        _context.Labels.Remove(existingLabel);
        await _context.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Label deleted successfully", 204);
    }

    private static LabelDto MapToDto(Label label)
    {
        return new LabelDto
        (
            label.Id,
            label.Name,
            label.Color,
            label.CreatedAt,
            label.UpdatedAt,
            label.CreatedBy,
            label.UpdatedBy
        );
    }

    private static Label MapToEntity(CreateLabelDto createDto, Guid actorId)
    {
        return new Label
        {
            Id = Guid.NewGuid(),
            Name = createDto.Name!,
            Color = createDto.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
