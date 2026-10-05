using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICardService
{
    Task<ApiResponse<CardDto>> CreateAsync(CreateCardDto createCardDto);
    Task<ApiResponse<CardDto>> UpdateAsync(Guid id, UpdateCardDto updateCardDto);
    Task<ApiResponse<CardDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<PagedResponse<CardDto>>> GetAllAsync(CardSearchDto searchDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}
public class CardService: ICardService
{
    private readonly ApplicationDbContext _context;

    public CardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<CardDto>> CreateAsync(CreateCardDto createCardDto)
    {
        var (labels, missingLabelIds) = await FindLabelsAsync(createCardDto.LabelIds);
        if (missingLabelIds.Count > 0)
        {
            return ApiResponse<CardDto>.ErrorResponse("Label not found", 400, missingLabelIds);
        }

        var card = MapToEntity(createCardDto);
        card.Labels.AddRange(labels);

        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        var cardDto = MapToDto(card);

        return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card created successfully", 201);
    }

    public async Task<ApiResponse<CardDto>> UpdateAsync(Guid id, UpdateCardDto updateCardDto)
    {
        var card = await _context.Cards
            .Include(c => c.Labels)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (card == null)
        {
            return ApiResponse<CardDto>.ErrorResponse("Card not found", 404);
        }

        var (labels, missingLabelIds) = await FindLabelsAsync(updateCardDto.LabelIds);
        if (missingLabelIds.Count > 0)
        {
            return ApiResponse<CardDto>.ErrorResponse("Label not found", 400, missingLabelIds);
        }

        card.Title = updateCardDto.Title;
        card.Description = updateCardDto.Description;
        card.DueDate = updateCardDto.DueDate;
        card.Position = updateCardDto.Position;
        card.ListId = updateCardDto.ListId;
        card.Status = updateCardDto.Status;
        card.Cover = updateCardDto.Cover;
        card.StartDate = updateCardDto.StartDate;
        card.EndDate = updateCardDto.EndDate;
        card.ReminderDate = updateCardDto.ReminderDate;
        card.IsArchived = updateCardDto.IsArchived;
        card.UpdatedAt = DateTime.UtcNow;
        card.Labels.Clear();
        card.Labels.AddRange(labels);

        await _context.SaveChangesAsync();

        var cardDto = MapToDto(card);

        return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card updated successfully");
    }
    public async Task<ApiResponse<CardDto>> GetByIdAsync(Guid id)
    {
        var card = await _context.Cards
            .Include(c => c.Labels)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (card == null)
        {
            return ApiResponse<CardDto>.ErrorResponse("Card not found", 404);
        }

        var cardDto = MapToDto(card);

        return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card retrieved successfully");
    }
    public async Task<ApiResponse<PagedResponse<CardDto>>> GetAllAsync(CardSearchDto searchDto)
    {
        var query = _context.Cards.AsQueryable();

        if (!string.IsNullOrEmpty(searchDto.searchTerm))
        {
            query = query.Where(c => c.Title.Contains(searchDto.searchTerm) || c.Description.Contains(searchDto.searchTerm));
        }

        if (searchDto.DueDate.HasValue)
        {
            query = query.Where(c => c.DueDate.Date == searchDto.DueDate.Value.Date);
        }

        if (searchDto.Position.HasValue)
        {
            query = query.Where(c => c.Position == searchDto.Position.Value);
        }

        if (searchDto.ListId.HasValue)
        {
            query = query.Where(c => c.ListId == searchDto.ListId.Value);
        }

        if (searchDto.Status.HasValue)
        {
            query = query.Where(c => c.Status == searchDto.Status.Value);
        }

        if (searchDto.StartDate.HasValue)
        {
            query = query.Where(c => c.StartDate >= searchDto.StartDate.Value);
        }

        if (searchDto.EndDate.HasValue)
        {
            query = query.Where(c => c.EndDate <= searchDto.EndDate.Value);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);

        var cards = await query
            .OrderBy(c => c.Position)
            .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
            .Take(searchDto.PageSize)
            .Include(c => c.Labels)
            .ToListAsync();

        var cardDtos = cards.Select(MapToDto).ToList();

        var pagedResponse = PagedResponse<CardDto>.SuccessResponse(
            cardDtos,
            totalCount,
            searchDto.PageSize,
            searchDto.PageNumber,
            "Cards retrieved successfully"
        );

        return ApiResponse<PagedResponse<CardDto>>.SuccessResponse(pagedResponse, "Cards retrieved successfully");
    }
    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var card = await _context.Cards.FindAsync(id);
        if (card == null)
        {
            return ApiResponse<bool>.ErrorResponse("Card not found", 404);
        }

        _context.Cards.Remove(card);
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Card deleted successfully", 204);
    }
    private async Task<(List<Label> Labels, List<string> MissingIds)> FindLabelsAsync(List<Guid>? labelIds)
    {
        var ids = labelIds?.Distinct().ToList() ?? [];
        var labels = await _context.Labels.Where(l => ids.Contains(l.Id)).ToListAsync();
        var missingIds = ids.Except(labels.Select(l => l.Id)).Select(id => id.ToString()).ToList();
        return (labels, missingIds);
    }
    private static CardDto MapToDto(Card card)
    {
        return new CardDto
        (
            card.Id,
            card.Title,
            card.Description,
            card.DueDate,
            card.Position,
            card.ListId,
            card.Status,
            card.Cover,
            card.StartDate,
            card.EndDate,
            card.ReminderDate,
            card.IsArchived,
            card.Labels.Select(l => l.Id).ToList(),
            card.CreatedAt,
            card.UpdatedAt,
            card.CreatedBy,
            card.UpdatedBy
        );
    }
    private static Card MapToEntity(CreateCardDto createCardDto)
    {
        return new Card
        {
            Id = Guid.NewGuid(),
            Title = createCardDto.Title,
            Description = createCardDto.Description,
            DueDate = createCardDto.DueDate,
            Position = createCardDto.Position,
            ListId = createCardDto.ListId,
            Status = createCardDto.Status,
            Cover = createCardDto.Cover,
            StartDate = createCardDto.StartDate,
            EndDate = createCardDto.EndDate,
            ReminderDate = createCardDto.ReminderDate,
            IsArchived = createCardDto.IsArchived,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid()
        };
    }
}
