using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/cards/{cardId}/attachments")]
public class AttachmentsController : ApiControllerBase
{
    private readonly IAttachmentService _attachmentService;

    public AttachmentsController(IAttachmentService attachmentService)
    {
        _attachmentService = attachmentService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AttachmentDto>>>> GetAttachments(Guid cardId)
    {
        var response = await _attachmentService.GetAllAsync(cardId);
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AttachmentDto>>> GetAttachment(Guid cardId, Guid id)
    {
        var response = await _attachmentService.GetByIdAsync(cardId, id);
        return ToActionResult(response);
    }

    [HttpGet("{id}/content")]
    public async Task<ActionResult> GetAttachmentContent(Guid cardId, Guid id)
    {
        var response = await _attachmentService.GetContentUrlAsync(cardId, id);
        if (response.Data is not { } url)
        {
            return ToActionResult(response);
        }

        Response.Headers.CacheControl = "no-store";
        return Redirect(url);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [NoFormBinding]
    public async Task<ActionResult<ApiResponse<AttachmentDto>>> CreateFileAttachment(Guid cardId)
    {
        var form = await AttachmentForm.ReadAsync(Request, HttpContext.RequestAborted);
        if (form.Data is not { } upload)
        {
            return ToActionResult(
                ApiResponse<AttachmentDto>.ErrorResponse(form.Message, form.StatusCode, form.Errors));
        }

        var response = await _attachmentService.CreateFileAsync(
            cardId, upload, HttpContext.RequestAborted);
        if (!response.Success)
        {
            return ToActionResult(response);
        }

        return CreatedAtAction(nameof(GetAttachment), new { cardId, id = response.Data!.Id }, response);
    }

    [HttpPost]
    [Consumes("application/json")]
    [RequiresContentType]
    public async Task<ActionResult<ApiResponse<AttachmentDto>>> CreateLinkAttachment(
        Guid cardId, [FromBody] CreateLinkAttachmentDto createLinkAttachmentDto)
    {
        var response = await _attachmentService.CreateLinkAsync(cardId, CreateLinkAttachmentWrite.Of(createLinkAttachmentDto));
        if (!response.Success)
        {
            return ToActionResult(response);
        }

        return CreatedAtAction(nameof(GetAttachment), new { cardId, id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<AttachmentDto>>> UpdateAttachment(
        Guid cardId, Guid id, [FromBody] UpdateAttachmentDto updateAttachmentDto)
    {
        var response = await _attachmentService.UpdateAsync(cardId, id, UpdateAttachmentWrite.Of(updateAttachmentDto));
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAttachment(Guid cardId, Guid id)
    {
        var response = await _attachmentService.DeleteAsync(cardId, id);
        return ToActionResult(response);
    }
}
