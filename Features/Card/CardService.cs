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
        try
        {
            var card = MapToEntity(createCardDto);

            _context.Cards.Add(card);
            await _context.SaveChangesAsync();

            var cardDto = MapToDto(card);

            return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card created successfully", 201);

        }
        catch (Exception e)
        {
            return ApiResponse<CardDto>.ErrorResponse("An error occurred while creating the card", 500,
                new List<string> { e.Message });
        }
    }
    
    public async Task<ApiResponse<CardDto>> UpdateAsync(Guid id, UpdateCardDto updateCardDto)
    {
        try
        {
            var card = await _context.Cards.FindAsync(id);
            if (card == null)
            {
                return ApiResponse<CardDto>.ErrorResponse("Card not found", 404);
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

            _context.Cards.Update(card);
            await _context.SaveChangesAsync();

            var cardDto = MapToDto(card);

            return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card updated successfully");
        }
        catch (Exception e)
        {
            return ApiResponse<CardDto>.ErrorResponse("An error occurred while updating the card", 500,
                new List<string> { e.Message });
        }
    }
    public async Task<ApiResponse<CardDto>> GetByIdAsync(Guid id)
    {
        try
        {
            var card = await _context.Cards.FindAsync(id);
            if (card == null)
            {
                return ApiResponse<CardDto>.ErrorResponse("Card not found", 404);
            }

            var cardDto = MapToDto(card);

            return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card retrieved successfully");
        }
        catch (Exception e)
        {
            return ApiResponse<CardDto>.ErrorResponse("An error occurred while retrieving the card", 500,
                new List<string> { e.Message });
        }
    }
    public async Task<ApiResponse<PagedResponse<CardDto>>> GetAllAsync(CardSearchDto searchDto)
    {
        try
        {
            var query = _context.Cards.AsQueryable();

            if (!string.IsNullOrEmpty(searchDto.SearchTerm))
            {
                query = query.Where(c => c.Title.Contains(searchDto.SearchTerm) || c.Description.Contains(searchDto.SearchTerm));
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
                .ToListAsync();

            var cardDtos = cards.Select(MapToDto).ToList();

            var pagedResponse = new PagedResponse<CardDto>
            {
                Data = cardDtos,
                TotalCount = totalCount,
                PageSize = searchDto.PageSize,
                CurrentPage = searchDto.PageNumber,
                TotalPages = totalPages
            };

            return ApiResponse<PagedResponse<CardDto>>.SuccessResponse(pagedResponse, "Cards retrieved successfully");
        }
        catch (Exception e)
        {
            return ApiResponse<PagedResponse<CardDto>>.ErrorResponse("An error occurred while retrieving cards", 500,
                new List<string> { e.Message });
        }
    }
    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        try
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
        catch (Exception e)
        {
            return ApiResponse<bool>.ErrorResponse("An error occurred while deleting the card", 500,
                new List<string> { e.Message });
        }
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
            CreatedBy = Guid.NewGuid() // Assuming the creator's ID is set here
        };
    }
