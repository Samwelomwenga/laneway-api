using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICardService
{
    Task<ApiResponse<CardDto>> CreateAsync(CreateCardDto createCardDto);
    Task<ApiResponse<CardDto>> UpdateAsync(Guid id, UpdateCardDto updateCardDto);
    Task<ApiResponse<CardDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<List<CardDto>>> GetAllAsync(CardSearchDto searchDto);
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
        var (labels, referenceErrors) = await ResolveReferencesAsync(createCardDto.ListId!.Value, createCardDto.LabelIds);
        if (referenceErrors.Count > 0)
        {
            return ApiResponse<CardDto>.ErrorResponse("A referenced resource does not exist", 400, referenceErrors);
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

        var (labels, referenceErrors) = await ResolveReferencesAsync(updateCardDto.ListId!.Value, updateCardDto.LabelIds);
        if (referenceErrors.Count > 0)
        {
            return ApiResponse<CardDto>.ErrorResponse("A referenced resource does not exist", 400, referenceErrors);
        }

        card.Title = updateCardDto.Title!;
        card.Description = updateCardDto.Description ?? string.Empty;
        card.DueDate = updateCardDto.DueDate;
        card.Position = updateCardDto.Position!.Value;
        card.ListId = updateCardDto.ListId.Value;
        card.IsDueComplete = updateCardDto.IsDueComplete!.Value;
        card.Cover = updateCardDto.Cover;
        card.StartDate = updateCardDto.StartDate;
        card.DueReminderMinutes = updateCardDto.DueReminderMinutes;
        card.IsArchived = updateCardDto.IsArchived!.Value;
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
    public async Task<ApiResponse<List<CardDto>>> GetAllAsync(CardSearchDto searchDto)
    {
        if (searchDto.ListId is { } filterListId && !await _context.Lists.AnyAsync(l => l.Id == filterListId))
        {
            return ReferenceErrors.NotFound<List<CardDto>>("listId", "List", filterListId);
        }

        var query = _context.Cards.AsQueryable();

        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(c => c.Title.ToLower().Contains(term.ToLower()) ||
                                     c.Description.ToLower().Contains(term.ToLower()));
        }

        if (searchDto.ListId is { } listId)
        {
            query = query.Where(c => c.ListId == listId);
        }

        if (searchDto.IsArchived is { } isArchived)
        {
            query = query.Where(c => c.IsArchived == isArchived);
        }

        if (searchDto.DueDate is { } dueDate)
        {
            var dayStart = DateTime.SpecifyKind(dueDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(c => c.DueDate >= dayStart && c.DueDate < dayEnd);
        }

        if (searchDto.DueBefore is { } dueBefore)
        {
            var before = dueBefore.UtcDateTime;
            query = query.Where(c => c.DueDate < before);
        }

        if (searchDto.StartFrom is { } startFrom)
        {
            var from = startFrom.UtcDateTime;
            query = query.Where(c => c.StartDate >= from);
        }

        if (searchDto.IsDueComplete is { } isDueComplete)
        {
            query = query.Where(c => c.DueDate != null && c.IsDueComplete == isDueComplete);
        }

        var (cards, totalCount) = await PagedQuery.ReadAsync(
            query.OrderBy(c => c.Position).Include(c => c.Labels),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        return PagedResponse<CardDto>.SuccessResponse(
            cards.Select(MapToDto).ToList(),
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Cards retrieved successfully"
        );
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
    private async Task<(List<Label> Labels, List<ApiError> Errors)> ResolveReferencesAsync(Guid listId, List<Guid?>? labelIds)
    {
        var errors = new List<ApiError>();
        if (!await _context.Lists.AnyAsync(l => l.Id == listId))
        {
            errors.Add(new ApiError("listId", ErrorCodes.NotFound, $"List {listId} does not exist."));
        }

        var ids = labelIds?.Select(id => id!.Value).ToList() ?? [];
        var labels = await _context.Labels.Where(l => ids.Contains(l.Id)).ToListAsync();
        var foundIds = labels.Select(l => l.Id).ToHashSet();
        errors.AddRange(ids
            .Select((id, index) => (Id: id, Index: index))
            .Where(entry => !foundIds.Contains(entry.Id))
            .DistinctBy(entry => entry.Id)
            .Select(entry => new ApiError($"labelIds[{entry.Index}]", ErrorCodes.NotFound, $"Label {entry.Id} does not exist.")));
        return (labels, errors);
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
            card.IsDueComplete,
            card.Cover,
            card.StartDate,
            card.DueReminderMinutes,
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
            Title = createCardDto.Title!,
            Description = createCardDto.Description ?? string.Empty,
            DueDate = createCardDto.DueDate,
            Position = createCardDto.Position!.Value,
            ListId = createCardDto.ListId!.Value,
            IsDueComplete = createCardDto.IsDueComplete!.Value,
            Cover = createCardDto.Cover,
            StartDate = createCardDto.StartDate,
            DueReminderMinutes = createCardDto.DueReminderMinutes,
            IsArchived = createCardDto.IsArchived!.Value,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid()
        };
    }
}
