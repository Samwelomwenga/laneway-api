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
    private readonly Actor _actor;
    private readonly Placements _placements;

    public CardService(ApplicationDbContext context, Actor actor, Placements placements)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
    }

    public async Task<ApiResponse<CardDto>> CreateAsync(CreateCardDto createCardDto)
    {
        var listId = createCardDto.ListId!.Value;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var (labels, listExists, errors) = await ResolveReferencesAsync(listId, createCardDto.LabelIds);
        var position = 0d;
        if (listExists)
        {
            var placed = await _placements.ResolveInListAsync(
                listId, new Placement(createCardDto.Position, createCardDto.Before, createCardDto.After));
            errors.AddRange(placed.Errors);
            position = placed.Position;
        }

        if (errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CardDto>(errors);
        }

        var card = MapToEntity(createCardDto, _actor.Id, position);
        card.Labels.AddRange(labels);

        _context.Cards.Add(card);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

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

        var (labels, _, referenceErrors) = await ResolveReferencesAsync(updateCardDto.ListId!.Value, updateCardDto.LabelIds);
        if (referenceErrors.Count > 0)
        {
            return ReferenceErrors.Invalid<CardDto>(referenceErrors);
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

        var labelsChanged = !card.Labels.Select(label => label.Id).ToHashSet().SetEquals(labels.Select(label => label.Id));
        card.Labels.Clear();
        card.Labels.AddRange(labels);
        _context.StampChange(card, _actor, relatedChanged: labelsChanged);

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
            query.InSortOrder().Include(c => c.Labels),
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
    private async Task<(List<Label> Labels, bool ListExists, List<ApiError> Errors)> ResolveReferencesAsync(
        Guid listId, List<Guid?>? labelIds)
    {
        var errors = new List<ApiError>();
        var listExists = await _context.Lists.AnyAsync(l => l.Id == listId);
        if (!listExists)
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
        return (labels, listExists, errors);
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
    private static Card MapToEntity(CreateCardDto createCardDto, Guid actorId, double position)
    {
        return new Card
        {
            Id = Guid.NewGuid(),
            Title = createCardDto.Title!,
            Description = createCardDto.Description ?? string.Empty,
            DueDate = createCardDto.DueDate,
            Position = position,
            ListId = createCardDto.ListId!.Value,
            IsDueComplete = createCardDto.IsDueComplete!.Value,
            Cover = createCardDto.Cover,
            StartDate = createCardDto.StartDate,
            DueReminderMinutes = createCardDto.DueReminderMinutes,
            IsArchived = createCardDto.IsArchived!.Value,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
