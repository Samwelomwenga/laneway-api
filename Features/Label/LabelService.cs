namespace DefaultNamespace;

public interface ILabelService
{
    Task<List<Label>> GetAllAsync(LabelSearchDto searchDto);
    Task<Label?> GetByIdAsync(Guid id);
    Task<Label> CreateAsync(Label label);
    Task<Label> UpdateAsync(Label label);
    Task DeleteAsync(Guid id);
}

public class LabelService : ILabelService
{
    private readonly ApplicationDbContext _context;

    public LabelService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Label>> GetAllAsync(LabelSearchDto searchDto)
    {
        try
        {
            const query = _context.Label.AsQueryable();
            if (!string.IsNullOrEmpty(searchDto.Name))
            {
                query = query.Where(l => l.Name.Contains(searchDto.Name, StringComparison.OrdinalIgnoreCase));
            }
            
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);
            var labels = await query
                .OrderBy(l => l.Name)
                .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                .Take(searchDto.PageSize)
                .ToListAsync();
            var labelDtos = labels.Select(MapToDto).ToList();
            return new PagedResult<LabelDto>
            {
                Data = labelDtos,
                TotalCount = totalCount,
                PageSize = searchDto.PageSize,
                CurrentPage = searchDto.PageNumber,
                TotalPages = totalPages,
            };
            
            

        }
        catch (Exception e)
        {
            return <ApiResponse<List<LabelDto>>>.ErrorResponse("An error occurred while retrieving labels", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<Label?> GetByIdAsync(Guid id)
    {
        try
        {
            var label = await _context.Labels.FindAsync(id);
            if (label == null)
            {
                return ApiResponse<LabelDto>.ErrorResponse("Label not found", 404);
            }
            var labelDto = MapToDto(label);
            return ApiResponse<LabelDto>.SuccessResponse(labelDto, "Label retrieved successfully", 200);

        }
        catch (Exception e)
        {
           return <ApiResponse<LabelDto>>.ErrorResponse("An error occurred while retrieving the label", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<Label> CreateAsync(Label label)
    {
        try
        {
            var existingLabel = await _context.Labels
                .FirstOrDefaultAsync(l => l.Name.Equals(label.Name, StringComparison.OrdinalIgnoreCase));
            if (existingLabel != null)
            {
                return <ApiResponse<LabelDto>>.ErrorResponse("Label with the same name already exists", 400,
                    new List<string> { "A label with this name already exists." });
            }
           var newLabel = MapToEntity(label);
            _context.Labels.Add(newLabel);
            await _context.SaveChangesAsync();
           var LabelDto = MapToDto(newLabel);
            return ApiResponse<LabelDto>.SuccessResponse(labelDto, "Label created successfully", 201);

        }
        catch (Exception e)
        {
           return <ApiResponse<LabelDto>>.ErrorResponse("An error occurred while creating the label", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<Label> UpdateAsync(Label label)
    {
        try
        {
            var existingLabel = await _context.Labels.FindAsync(label.Id);
            if (existingLabel == null)
            {
                return <ApiResponse<LabelDto>>.ErrorResponse("Label not found", 404);
            }

            existingLabel.Name = label.Name;
            existingLabel.Color = label.Color;
            existingLabel.UpdatedAt = DateTime.UtcNow;
            existingLabel.UpdatedBy = Guid.NewGuid(); // Replace with actual user ID

            _context.Labels.Update(existingLabel);
            await _context.SaveChangesAsync();
            
            var labelDto = MapToDto(existingLabel);
            return ApiResponse<LabelDto>.SuccessResponse(labelDto, "Label updated successfully", 200);

        }
        catch (Exception e)
        {
            return <ApiResponse<LabelDto>>.ErrorResponse("An error occurred while updating the label", 500,
                new List<string> { e.Message });
        }
    }

    public async Task DeleteAsync(Guid id)
    {
       var existingLabel = await _context.Labels.FindAsync(id);
        if (existingLabel == null)
        {
            return <ApiResponse<LabelDto>>.ErrorResponse("Label not found", 404);
        }

        try
        {
            _context.Labels.Remove(existingLabel);
            await _context.SaveChangesAsync();
            return ApiResponse<LabelDto>.SuccessResponse(null, "Label deleted successfully", 204);
        }
        catch (Exception e)
        {
            return <ApiResponse<LabelDto>>.ErrorResponse("An error occurred while deleting the label", 500,
                new List<string> { e.Message });
        }
    }
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
        Name = createDto.Name,
        Color = createDto.Color,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Guid.NewGuid() // Replace with actual user ID
    };
}
