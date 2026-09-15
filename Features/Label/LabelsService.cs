using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ILabelService
{
    Task<PagedResponse<LabelDto>> GetAllAsync(LabelSearchDto searchDto);
    Task<ApiResponse<LabelDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<LabelDto>> CreateAsync(CreateLabelDto createLabelDto);
    Task<ApiResponse<LabelDto>> UpdateAsync(Guid id, UpdateLabelDto updateLabelDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class LabelService : ILabelService
{
    private readonly ApplicationDbContext _context;

    public LabelService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResponse<LabelDto>> GetAllAsync(LabelSearchDto searchDto)
    {
        var query = _context.Labels.AsQueryable();
        if (!string.IsNullOrEmpty(searchDto.SearchTerm))
        {
            query = query.Where(l => l.Name.ToLower().Contains(searchDto.SearchTerm.ToLower()));
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);
        var labels = await query
            .OrderBy(l => l.Name)
            .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
            .Take(searchDto.PageSize)
            .ToListAsync();
        var labelDtos = labels.Select(MapToDto).ToList();

        return PagedResponse<LabelDto>.SuccessResponse(
            labelDtos,
            totalCount,
            searchDto.PageSize,
            searchDto.PageNumber,
            "Labels retrieved successfully"
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
        var newLabel = MapToEntity(createLabelDto);
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
        existingLabel.UpdatedAt = DateTime.UtcNow;
        existingLabel.UpdatedBy = Guid.NewGuid();

        _context.Labels.Update(existingLabel);
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

    private static Label MapToEntity(CreateLabelDto createDto)
    {
        return new Label
        {
            Id = Guid.NewGuid(),
            Name = createDto.Name!,
            Color = createDto.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid()
        };
    }
}
