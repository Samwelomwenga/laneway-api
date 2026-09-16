using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICardService
{
    Task<ApiResponse<CardDto>> CreateAsync(CreateCardDto createCardDto);
    Task<ApiResponse<CardDto>> UpdateAsync(Guid id, UpdateCardDto updateCardDto);
    Task<ApiResponse<CardDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<List<CardDto>>> GetAllAsync(CardSearchDto searchDto);
    Task<ApiResponse<bool>> AddLabelAsync(Guid cardId, Guid labelId);
    Task<ApiResponse<bool>> RemoveLabelAsync(Guid cardId, Guid labelId);
    Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedDto archivedDto);
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

        query = ArchiveView.Cards(query, searchDto.Archived, _context);

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
    public Task<ApiResponse<bool>> AddLabelAsync(Guid cardId, Guid labelId) =>
        ChangeLabelAsync(cardId, labelId, add: true);

    public Task<ApiResponse<bool>> RemoveLabelAsync(Guid cardId, Guid labelId) =>
        ChangeLabelAsync(cardId, labelId, add: false);

    private async Task<ApiResponse<bool>> ChangeLabelAsync(Guid cardId, Guid labelId, bool add)
    {
        var card = await _context.Cards
            .Include(c => c.Labels)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null)
        {
            return ApiResponse<bool>.ErrorResponse("Card not found", 404);
        }

        var label = await _context.Labels.FindAsync(labelId);
        if (label == null)
        {
            return ApiResponse<bool>.ErrorResponse("Label not found", 404);
        }

        var boardId = await BoardOfListAsync(card.ListId);
        if (boardId is { } board && label.BoardId != board)
        {
            return ReferenceErrors.Invalid<bool>([NotOnBoard("labelId", labelId, board)]);
        }

        var message = add ? "Label added to the card" : "Label removed from the card";
        var onCard = card.Labels.Find(l => l.Id == labelId);
        var changesTheSet = add ? onCard is null : onCard is not null;
        if (!changesTheSet)
        {
            return ApiResponse<bool>.SuccessResponse(true, message, 204);
        }

        if (add)
        {
            card.Labels.Add(label);
        }
        else
        {
            card.Labels.Remove(onCard!);
        }

        _context.StampChange(card, _actor, relatedChanged: true);
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, message, 204);
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedDto archivedDto) =>
        await _context.SetArchivedAsync(await _context.Cards.FindAsync(id), _actor, archivedDto, "Card");

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var card = await _context.Cards.FindAsync(id);
        if (card == null)
        {
            return ApiResponse<bool>.ErrorResponse("Card not found", 404);
        }

        if (!card.IsArchived)
        {
            return ArchiveErrors.NotArchived<bool>("Card");
        }

        _context.Cards.Remove(card);
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Card deleted successfully", 204);
    }
    private async Task<(List<Label> Labels, bool ListExists, List<ApiError> Errors)> ResolveReferencesAsync(
        Guid listId, List<Guid?>? labelIds)
    {
        var errors = new List<ApiError>();
        var boardId = await BoardOfListAsync(listId);
        if (boardId is null)
        {
            errors.Add(new ApiError("listId", ErrorCodes.NotFound, $"List {listId} does not exist."));
        }

        var ids = labelIds?.Select(id => id!.Value).ToList() ?? [];
        var labels = await _context.Labels.Where(l => ids.Contains(l.Id)).ToListAsync();
        var seen = new HashSet<Guid>();
        for (var index = 0; index < ids.Count; index++)
        {
            var id = ids[index];
            if (!seen.Add(id))
            {
                continue;
            }

            var field = $"labelIds[{index}]";
            var label = labels.Find(l => l.Id == id);
            if (label is null)
            {
                errors.Add(new ApiError(field, ErrorCodes.NotFound, $"Label {id} does not exist."));
            }
            else if (boardId is { } board && label.BoardId != board)
            {
                errors.Add(NotOnBoard(field, id, board));
            }
        }

        return (labels, boardId is not null, errors);
    }

    private async Task<Guid?> BoardOfListAsync(Guid listId) =>
        await _context.Lists.Where(l => l.Id == listId).Select(l => (Guid?)l.BoardId).FirstOrDefaultAsync();

    private static ApiError NotOnBoard(string field, Guid labelId, Guid boardId) =>
        new(field, ErrorCodes.NotOnBoard, $"Label {labelId} isn't on board {boardId}.");
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
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
