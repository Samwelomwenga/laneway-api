using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/[controller]")]
public class CommentsController : ApiControllerBase
{
    private readonly ICommentService _commentService;

    public CommentsController(ICommentService commentService)
    {
        _commentService = commentService;
    }

    [HttpPost("/api/v1/cards/{cardId}/comments")]
    public async Task<ActionResult<ApiResponse<ActivityEntryDto>>> CreateComment(
        Guid cardId, [FromBody] CommentTextDto commentTextDto)
    {
        var response = await _commentService.CreateAsync(cardId, CommentTextWrite.Of(commentTextDto));
        return ToActionResult(response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ActivityEntryDto>>> UpdateComment(
        Guid id, [FromBody] CommentTextDto commentTextDto)
    {
        var response = await _commentService.UpdateAsync(id, CommentTextWrite.Of(commentTextDto));
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteComment(Guid id)
    {
        var response = await _commentService.DeleteAsync(id);
        return ToActionResult(response);
    }
}
