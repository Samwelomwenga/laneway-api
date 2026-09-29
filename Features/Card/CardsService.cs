using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICardService
{
    Task<ApiResponse<CardDto>> CreateAsync(CreateCardWrite write);
    Task<ApiResponse<CardDto>> UpdateAsync(Guid id, UpdateCardWrite write);
    Task<ApiResponse<CardDto>> MoveAsync(Guid id, MoveCardWrite write);
    Task<ApiResponse<CardDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<List<CardDto>>> GetAllAsync(CardSearchDto searchDto);
    Task<ApiResponse<bool>> AddLabelAsync(Guid cardId, Guid labelId);
    Task<ApiResponse<bool>> RemoveLabelAsync(Guid cardId, Guid labelId);
    Task<ApiResponse<bool>> SetCoverAsync(Guid id, CoverDto coverDto);
    Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}
public class CardService: ICardService
{
    private const int MoveAttempts = 3;

    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;
    private readonly ArchiveGuard _archive;
    private readonly LabelMatching _labelMatching;
    private readonly CardCompletion _completion;
    private readonly AttachmentCounts _attachments;
    private readonly CommentCounts _comments;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public CardService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        LabelMatching labelMatching,
        CardCompletion completion,
        AttachmentCounts attachments,
        CommentCounts comments,
        ActivityWriter activity,
        ActivityTree tree)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _labelMatching = labelMatching;
        _completion = completion;
        _attachments = attachments;
        _comments = comments;
        _activity = activity;
        _tree = tree;
    }

    public async Task<ApiResponse<CardDto>> CreateAsync(CreateCardWrite write)
    {
        var listId = write.ListId;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var (labels, listExists, errors) = await ResolveReferencesAsync(listId, write.LabelIds);
        var position = 0d;
        if (listExists)
        {
            var placed = await _placements.ResolveInListAsync(listId, Placement.Of(write));
            errors.AddRange(placed.Errors);
            position = placed.Position;
        }

        if (errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CardDto>(errors);
        }

        if (await _archive.OnListAsync(listId) is { } archived)
        {
            return ArchiveErrors.NoCreate<CardDto>(archived, "listId", TreeItem.Card);
        }

        var card = MapToEntity(write, _actor.Id, position);
        card.Labels.AddRange(labels);

        _context.Cards.Add(card);
        var chain = await _tree.ListAsync(listId);
        await _activity.AddAsync(
            ActivityType.CreateCard,
            chain.PlaceOn(card.Id),
            actor => new CreateCardData(
                actor,
                chain.Workspace,
                chain.Board,
                chain.List,
                CardRef.Of(card),
                labels.Count > 0 ? LabelRefs(labels) : null));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var cardDto = CardView.Of(card, default, attachmentCount: 0, commentCount: 0);

        return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card created successfully", 201);
    }

    public async Task<ApiResponse<CardDto>> UpdateAsync(Guid id, UpdateCardWrite write)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var card = await _context.Cards
            .Include(c => c.Labels)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (card == null)
        {
            return ApiResponse<CardDto>.ErrorResponse("Card not found", 404);
        }

        if (await _archive.OnCardAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<CardDto>(archived, TreeItem.Card);
        }

        var (labels, _, referenceErrors) = await ResolveReferencesAsync(card.ListId, write.LabelIds);
        if (referenceErrors.Count > 0)
        {
            return ReferenceErrors.Invalid<CardDto>(referenceErrors);
        }

        var watch = TouchesCompletion(card, write) ? await _completion.WatchAsync(id) : null;

        card.Title = write.Title;
        card.Description = write.Description;
        card.DueDate = write.DueDate;
        card.IsDueComplete = write.IsDueComplete;
        card.StartDate = write.StartDate;
        card.DueReminderMinutes = write.DueReminderMinutes;

        var labelsChanged = !card.Labels.Select(label => label.Id).ToHashSet().SetEquals(labels.Select(label => label.Id));
        var oldLabels = labelsChanged ? LabelRefs(card.Labels) : null;
        card.Labels.Clear();
        card.Labels.AddRange(labels);
        _context.StampChange(card, _actor, relatedChanged: labelsChanged);

        var tracked = _context.Entry(card);
        if (tracked.Changed())
        {
            var old = CardFields.Changed(tracked, oldLabels);
            var chain = await _tree.ListAsync(card.ListId);
            await _activity.AddAsync(
                ActivityType.UpdateCard,
                chain.PlaceOn(card.Id),
                actor => new UpdateCardData(
                    actor,
                    chain.Workspace,
                    chain.Board,
                    chain.List,
                    CardRef.Of(card),
                    labelsChanged ? LabelRefs(labels) : null,
                    old,
                    watch?.Change()));
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var cardDto = CardView.Of(
            card,
            await _completion.TallyAsync(id),
            await _attachments.OnCardAsync(id),
            await _comments.OnCardAsync(id));

        return ApiResponse<CardDto>.SuccessResponse(cardDto, "Card updated successfully");
    }

    public async Task<ApiResponse<CardDto>> MoveAsync(Guid id, MoveCardWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var listId = write.ListId;
        var placement = Placement.Of(write);

        for (var attempt = 1; attempt <= MoveAttempts; attempt++)
        {
            if (await MoveOnceAsync(id, listId, placement) is { } response)
            {
                return response;
            }

            _context.ChangeTracker.Clear();
        }

        throw new InvalidOperationException(
            $"Card {id} could not move to list {listId}. The list kept changing board.");
    }

    private async Task<ApiResponse<CardDto>?> MoveOnceAsync(Guid id, Guid listId, Placement placement)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var card = await _context.Cards
            .Include(c => c.Labels)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (card == null)
        {
            return ApiResponse<CardDto>.ErrorResponse("Card not found", 404);
        }

        if (await _archive.OnCardAsync(id) is { } readOnly)
        {
            return ArchiveErrors.ReadOnly<CardDto>(readOnly, TreeItem.Card);
        }

        var sourceBoardId = await _context.BoardOfListAsync(card.ListId);
        if (await _context.BoardOfListAsync(listId) is not { } boardId)
        {
            return ReferenceErrors.NotFound<CardDto>("listId", "List", listId);
        }

        var crossesBoards = boardId != sourceBoardId;
        if (crossesBoards)
        {
            await _context.TryLockBoardAsync(boardId);
        }

        var placed = await _placements.ResolveMoveInListAsync(card, listId, placement);

        var listChangedBoard = await _context.BoardOfListAsync(listId) != boardId;
        if (listChangedBoard)
        {
            return null;
        }

        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CardDto>(placed.Errors);
        }

        if (await _archive.OnListAsync(listId) is { } archived)
        {
            return ArchiveErrors.NoCreate<CardDto>(archived, "listId", TreeItem.Card);
        }

        var swaps = new List<LabelSwap>();
        if (crossesBoards)
        {
            var matches = await _labelMatching.CarryToBoardAsync([card], boardId);
            swaps.AddRange(matches.ConvertAll(match =>
                new LabelSwap(LabelRef.Of(match.From), LabelRef.Of(match.To), match.Created)));
        }

        var leaving = card.ListId;
        var wasAt = card.Position;
        card.ListId = listId;
        card.Position = placed.Position;
        _context.StampChange(card, _actor);

        if (_context.Entry(card).Changed())
        {
            var to = await _tree.ListAsync(listId);
            var origin = leaving == listId
                ? null
                : CardOrigin.Between(await _tree.ListAsync(leaving), to);
            await _activity.AddAsync(
                ActivityType.MoveCard,
                new ActivityPlace(
                    WorkspaceId: to.Workspace.Id,
                    BoardId: to.Board.Id,
                    ListId: to.List.Id,
                    CardId: card.Id,
                    FromWorkspaceId: origin?.Workspace?.Id,
                    FromBoardId: origin?.Board?.Id,
                    FromListId: origin?.List.Id),
                actor => new MoveCardData(
                    actor,
                    to.Workspace,
                    to.Board,
                    to.List,
                    CardRef.Of(card),
                    new PositionChange(wasAt, card.Position),
                    origin,
                    swaps.Count > 0 ? swaps : null));
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<CardDto>.SuccessResponse(
            CardView.Of(
                card,
                await _completion.TallyAsync(id),
                await _attachments.OnCardAsync(id),
                await _comments.OnCardAsync(id)),
            "Card moved successfully");
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

        var cardDto = CardView.Of(
            card,
            await _completion.TallyAsync(id),
            await _attachments.OnCardAsync(id),
            await _comments.OnCardAsync(id));

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
            query = query.Where(_completion.Matching(isDueComplete));
        }

        var (cards, totalCount) = await PagedQuery.ReadAsync(
            query.InSortOrder().Include(c => c.Labels),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        var cardIds = cards.ConvertAll(c => c.Id);
        var tallies = await _completion.TallyAsync(cardIds);
        var attachmentCounts = await _attachments.OnCardsAsync(cardIds);
        var commentCounts = await _comments.OnCardsAsync(cardIds);

        return PagedResponse<CardDto>.SuccessResponse(
            cards.ConvertAll(card => CardView.Of(
                card,
                tallies.GetValueOrDefault(card.Id),
                attachmentCounts.GetValueOrDefault(card.Id),
                commentCounts.GetValueOrDefault(card.Id))),
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

        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.Card);
        }

        var boardId = await _context.BoardOfListAsync(card.ListId);
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
        var chain = await _tree.ListAsync(card.ListId);
        await _activity.AddAsync(
            add ? ActivityType.AddLabelToCard : ActivityType.RemoveLabelFromCard,
            chain.PlaceOn(card.Id),
            actor => new CardLabelData(
                actor,
                chain.Workspace,
                chain.Board,
                chain.List,
                CardRef.Of(card),
                LabelRef.Of(label)));
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, message, 204);
    }

    public async Task<ApiResponse<bool>> SetCoverAsync(Guid id, CoverDto coverDto)
    {
        ArgumentNullException.ThrowIfNull(coverDto);

        var card = await _context.Cards.FindAsync(id);
        if (card == null)
        {
            return ApiResponse<bool>.ErrorResponse("Card not found", 404);
        }

        if (await _archive.OnCardAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.Card);
        }

        if (coverDto.AttachmentId is { } attachmentId && await CoverErrorAsync(id, attachmentId) is { } error)
        {
            return ReferenceErrors.Invalid<bool>([error]);
        }

        var wasAttachmentId = card.CoverAttachmentId;
        var wasColor = card.CoverColor;
        card.CoverAttachmentId = coverDto.AttachmentId;
        card.CoverColor = coverDto.Color;
        _context.StampChange(card, _actor);

        if (_context.Entry(card).Changed())
        {
            var cover = CoverRef.Of(await CoverRefAsync(card.CoverAttachmentId), card.CoverColor);
            var was = CoverRef.Of(await CoverRefAsync(wasAttachmentId), wasColor);
            var chain = await _tree.ListAsync(card.ListId);
            await _activity.AddAsync(
                ActivityType.UpdateCardCover,
                chain.PlaceOn(card.Id),
                actor => new UpdateCardCoverData(
                    actor,
                    chain.Workspace,
                    chain.Board,
                    chain.List,
                    CardRef.Of(card),
                    cover,
                    was));
        }

        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(
            true, coverDto is { AttachmentId: null, Color: null } ? "Cover cleared" : "Cover set", 204);
    }

    private async Task<AttachmentRef?> CoverRefAsync(Guid? attachmentId) =>
        attachmentId is { } coveredBy ? await _tree.AttachmentRefAsync(coveredBy) : null;

    private async Task<ApiError?> CoverErrorAsync(Guid cardId, Guid attachmentId)
    {
        var attachment = await _context.Attachments
            .Where(a => a.Id == attachmentId)
            .Select(a => new { a.CardId, a.MimeType })
            .FirstOrDefaultAsync();

        if (attachment is null)
        {
            return new ApiError("attachmentId", ErrorCodes.NotFound, $"Attachment {attachmentId} does not exist.");
        }

        if (attachment.CardId != cardId)
        {
            return new ApiError("attachmentId", ErrorCodes.NotOnCard,
                $"Attachment {attachmentId} isn't on card {cardId}.");
        }

        return AttachmentFileTypes.IsImage(attachment.MimeType)
            ? null
            : new ApiError("attachmentId", ErrorCodes.NotImage,
                $"A cover has to be a file of one of these types: {AttachmentFileTypes.ImageExtensions}.");
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived)
    {
        var card = await _context.Cards.FindAsync(id);
        if (card == null)
        {
            return ApiResponse<bool>.ErrorResponse("Card not found", 404);
        }

        if (await _archive.OnListAsync(card.ListId) is { } holder)
        {
            return ArchiveErrors.RestoreFirst<bool>(holder, TreeItem.Card);
        }

        return await _context.SetArchivedAsync(card, _actor, archived, "Card", async archiving =>
        {
            var chain = await _tree.ListAsync(card.ListId);
            await _activity.AddAsync(
                archiving ? ActivityType.ArchiveCard : ActivityType.RestoreCard,
                chain.PlaceOn(card.Id),
                actor => new ArchiveCardData(
                    actor, chain.Workspace, chain.Board, chain.List, CardRef.Of(card)));
        });
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var card = await _context.Cards.FindAsync(id);
        if (card == null)
        {
            return ApiResponse<bool>.ErrorResponse("Card not found", 404);
        }

        if (!card.IsArchived)
        {
            return ArchiveErrors.NotArchived<bool>(TreeItem.Card);
        }

        var chain = await _tree.ListAsync(card.ListId);
        _context.Cards.Remove(card);
        await _activity.AddAsync(
            ActivityType.DeleteCard,
            chain.PlaceOn(card.Id),
            actor => new DeleteCardData(actor, chain.Workspace, chain.Board, chain.List, CardRef.Of(card)));
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Card deleted successfully", 204);
    }

    private static bool TouchesCompletion(Card card, UpdateCardWrite write) =>
        card.DueDate != write.DueDate || card.IsDueComplete != write.IsDueComplete;

    private static IReadOnlyList<LabelRef> LabelRefs(List<Label> labels) => labels.ConvertAll(LabelRef.Of);

    private async Task<(List<Label> Labels, bool ListExists, List<ApiError> Errors)> ResolveReferencesAsync(
        Guid listId, List<Guid>? labelIds)
    {
        var errors = new List<ApiError>();
        var boardId = await _context.BoardOfListAsync(listId);
        if (boardId is null)
        {
            errors.Add(new ApiError("listId", ErrorCodes.NotFound, $"List {listId} does not exist."));
        }

        var ids = labelIds ?? [];
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

    private static ApiError NotOnBoard(string field, Guid labelId, Guid boardId) =>
        new(field, ErrorCodes.NotOnBoard, $"Label {labelId} isn't on board {boardId}.");
    private static Card MapToEntity(CreateCardWrite write, Guid actorId, double position)
    {
        return new Card
        {
            Id = Guid.NewGuid(),
            Title = write.Title,
            Description = write.Description,
            DueDate = write.DueDate,
            Position = position,
            ListId = write.ListId,
            IsDueComplete = write.IsDueComplete,
            StartDate = write.StartDate,
            DueReminderMinutes = write.DueReminderMinutes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
