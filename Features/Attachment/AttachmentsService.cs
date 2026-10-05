using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IAttachmentService
{
    Task<ApiResponse<AttachmentDto>> CreateFileAsync(Guid cardId, AttachmentUpload upload, CancellationToken token);
    Task<ApiResponse<AttachmentDto>> CreateLinkAsync(Guid cardId, CreateLinkAttachmentDto createLinkAttachmentDto);
    Task<ApiResponse<AttachmentDto>> UpdateAsync(Guid cardId, Guid id, UpdateAttachmentDto updateAttachmentDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid cardId, Guid id);
    Task<ApiResponse<AttachmentDto>> GetByIdAsync(Guid cardId, Guid id);
    Task<ApiResponse<List<AttachmentDto>>> GetAllAsync(Guid cardId);
    Task<ApiResponse<string>> GetContentUrlAsync(Guid cardId, Guid id);
}

public class AttachmentService : IAttachmentService
{
    private const int DownloadSeconds = 300;

    private static readonly TimeSpan UploadGrace = TimeSpan.FromHours(1);

    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly ArchiveGuard _archive;
    private readonly AttachmentStorage _storage;
    private readonly ILogger<AttachmentService> _logger;

    public AttachmentService(
        ApplicationDbContext context,
        Actor actor,
        ArchiveGuard archive,
        AttachmentStorage storage,
        ILogger<AttachmentService> logger)
    {
        _context = context;
        _actor = actor;
        _archive = archive;
        _storage = storage;
        _logger = logger;
    }

    public async Task<ApiResponse<AttachmentDto>> CreateFileAsync(
        Guid cardId, AttachmentUpload upload, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(upload);

        if (!await _context.Cards.AnyAsync(card => card.Id == cardId, token))
        {
            return CardNotFound<AttachmentDto>();
        }

        if (await RefusesAsync(cardId) is { } refused)
        {
            return refused;
        }

        var attachmentId = Guid.NewGuid();
        var objectKey = AttachmentStorage.KeyFor(cardId, attachmentId);
        var pending = await QueueUploadAsync(objectKey, token);
        await _storage.UploadAsync(objectKey, upload.Bytes, upload.MimeType, token);

        ApiResponse<AttachmentDto> created;
        try
        {
            created = await InsertAsync(cardId, attachmentId, objectKey, upload, pending, token);
        }
        catch
        {
            await RemoveNowAsync(objectKey);
            throw;
        }

        if (!created.Success)
        {
            await RemoveNowAsync(objectKey);
        }

        return created;
    }

    private async Task<ApiResponse<AttachmentDto>> InsertAsync(
        Guid cardId,
        Guid attachmentId,
        string objectKey,
        AttachmentUpload upload,
        PendingObjectDelete pending,
        CancellationToken token)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(token);

        if (!await _context.TryLockCardAsync(cardId))
        {
            return CardNotFound<AttachmentDto>();
        }

        if (await RefusesAsync(cardId) is { } refused)
        {
            return refused;
        }

        var attachment = MapFileToEntity(cardId, attachmentId, objectKey, upload);
        _context.Attachments.Add(attachment);
        _context.PendingObjectDeletes.Remove(pending);

        await _context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        return ApiResponse<AttachmentDto>.SuccessResponse(
            AttachmentView.Of(attachment), "Attachment created successfully", 201);
    }

    public async Task<ApiResponse<AttachmentDto>> CreateLinkAsync(
        Guid cardId, CreateLinkAttachmentDto createLinkAttachmentDto)
    {
        ArgumentNullException.ThrowIfNull(createLinkAttachmentDto);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.TryLockCardAsync(cardId))
        {
            return CardNotFound<AttachmentDto>();
        }

        if (await RefusesAsync(cardId) is { } refused)
        {
            return refused;
        }

        var attachment = MapLinkToEntity(cardId, createLinkAttachmentDto);
        _context.Attachments.Add(attachment);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ApiResponse<AttachmentDto>.SuccessResponse(
            AttachmentView.Of(attachment), "Attachment created successfully", 201);
    }

    public async Task<ApiResponse<AttachmentDto>> UpdateAsync(
        Guid cardId, Guid id, UpdateAttachmentDto updateAttachmentDto)
    {
        ArgumentNullException.ThrowIfNull(updateAttachmentDto);

        var attachment = await FindAsync(cardId, id);
        if (attachment == null)
        {
            return NotFound<AttachmentDto>();
        }

        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<AttachmentDto>(archived, TreeItem.Attachment);
        }

        attachment.Name = updateAttachmentDto.Name!;
        _context.StampChange(attachment, _actor);

        await _context.SaveChangesAsync();

        return ApiResponse<AttachmentDto>.SuccessResponse(
            AttachmentView.Of(attachment), "Attachment updated successfully");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid cardId, Guid id)
    {
        var attachment = await FindAsync(cardId, id);
        if (attachment == null)
        {
            return NotFound<bool>();
        }

        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<bool>(archived, TreeItem.Attachment);
        }

        _context.Attachments.Remove(attachment);
        await _context.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Attachment deleted successfully", 204);
    }

    public async Task<ApiResponse<AttachmentDto>> GetByIdAsync(Guid cardId, Guid id)
    {
        var attachment = await FindAsync(cardId, id);
        if (attachment == null)
        {
            return NotFound<AttachmentDto>();
        }

        return ApiResponse<AttachmentDto>.SuccessResponse(
            AttachmentView.Of(attachment), "Attachment retrieved successfully");
    }

    public async Task<ApiResponse<List<AttachmentDto>>> GetAllAsync(Guid cardId)
    {
        if (!await _context.Cards.AnyAsync(card => card.Id == cardId))
        {
            return CardNotFound<List<AttachmentDto>>();
        }

        var attachments = await _context.Attachments
            .Where(attachment => attachment.CardId == cardId)
            .OrderByDescending(attachment => attachment.CreatedAt)
            .ThenByDescending(attachment => attachment.Id)
            .ToListAsync();

        return ApiResponse<List<AttachmentDto>>.SuccessResponse(
            attachments.ConvertAll(AttachmentView.Of), "Attachments retrieved successfully");
    }

    public async Task<ApiResponse<string>> GetContentUrlAsync(Guid cardId, Guid id)
    {
        if (await FindAsync(cardId, id)
            is not { Kind: AttachmentKind.File, ObjectKey: { } objectKey, FileName: { } fileName })
        {
            return NotFound<string>();
        }

        var url = await _storage.SignedUrlAsync(objectKey, DownloadSeconds, fileName);
        return ApiResponse<string>.SuccessResponse(url, "Attachment content found", 302);
    }

    private async Task<ApiResponse<AttachmentDto>?> RefusesAsync(Guid cardId)
    {
        if (await _archive.OnCardAsync(cardId) is { } archived)
        {
            return ArchiveErrors.ReadOnly<AttachmentDto>(archived, TreeItem.Attachment);
        }

        return await IsFullAsync(cardId) ? Full<AttachmentDto>() : null;
    }

    private async Task<PendingObjectDelete> QueueUploadAsync(string objectKey, CancellationToken token)
    {
        var pending = new PendingObjectDelete
        {
            ObjectKey = objectKey,
            NotBefore = DateTime.UtcNow.Add(UploadGrace)
        };

        _context.PendingObjectDeletes.Add(pending);
        await _context.SaveChangesAsync(token);

        return pending;
    }

    private async Task RemoveNowAsync(string objectKey)
    {
        try
        {
            await _storage.RemoveAsync([objectKey]);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Could not remove {ObjectKey} after a failed create. The outbox will retry it.", objectKey);
        }
    }

    private Task<Attachment?> FindAsync(Guid cardId, Guid id) =>
        _context.Attachments.FirstOrDefaultAsync(
            attachment => attachment.Id == id && attachment.CardId == cardId);

    private async Task<bool> IsFullAsync(Guid cardId) =>
        await _context.Attachments.CountAsync(attachment => attachment.CardId == cardId)
        >= FieldLimits.AttachmentsPerCard;

    private static ApiResponse<T> CardNotFound<T>() => ApiResponse<T>.ErrorResponse("Card not found", 404);

    private static ApiResponse<T> NotFound<T>() => ApiResponse<T>.ErrorResponse("Attachment not found", 404);

    private static ApiResponse<T> Full<T>() =>
        ApiResponse<T>.ErrorResponse("The card is full", 409,
        [
            new ApiError(null, ErrorCodes.LimitReached,
                $"A card holds at most {FieldLimits.AttachmentsPerCard} attachments.")
        ]);

    private Attachment MapFileToEntity(Guid cardId, Guid id, string objectKey, AttachmentUpload upload)
    {
        return new Attachment
        {
            Id = id,
            CardId = cardId,
            Kind = AttachmentKind.File,
            Name = upload.Name,
            FileName = upload.FileName,
            MimeType = upload.MimeType,
            Bytes = upload.Bytes.Length,
            ObjectKey = objectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actor.Id
        };
    }

    private Attachment MapLinkToEntity(Guid cardId, CreateLinkAttachmentDto createLinkAttachmentDto)
    {
        var url = createLinkAttachmentDto.Url!;

        return new Attachment
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            Kind = AttachmentKind.Link,
            Name = string.IsNullOrEmpty(createLinkAttachmentDto.Name)
                ? url[..Math.Min(url.Length, FieldLimits.AttachmentName)]
                : createLinkAttachmentDto.Name,
            Url = url,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actor.Id
        };
    }
}
