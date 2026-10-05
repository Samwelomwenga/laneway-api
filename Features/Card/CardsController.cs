using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/[controller]")]
public class CardsController: ApiControllerBase
{
    private readonly ICardService _cardService;

    public CardsController(ICardService cardService)
    {
        _cardService = cardService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<CardDto>>> GetCards([FromQuery] CardSearchDto searchDto)
    {
        var response = await _cardService.GetAllAsync(searchDto);
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CardDto>>> GetCard(Guid id)
    {
        var response = await _cardService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CardDto>>> CreateCard([FromBody] CreateCardDto createCardDto)
    {
        var response = await _cardService.CreateAsync(createCardDto);
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetCard), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<CardDto>>> UpdateCard(Guid id, [FromBody] UpdateCardDto updateCardDto)
    {
        var response = await _cardService.UpdateAsync(id, updateCardDto);
        return ToActionResult(response);
    }

    [HttpPut("{id}/position")]
    public async Task<ActionResult<ApiResponse<CardDto>>> MoveCard(Guid id, [FromBody] MoveCardDto moveCardDto)
    {
        var response = await _cardService.MoveAsync(id, moveCardDto);
        return ToActionResult(response);
    }

    [HttpPost("{cardId}/labels/{labelId}")]
    public async Task<ActionResult<ApiResponse<bool>>> AddCardLabel(Guid cardId, Guid labelId)
    {
        var response = await _cardService.AddLabelAsync(cardId, labelId);
        return ToActionResult(response);
    }

    [HttpDelete("{cardId}/labels/{labelId}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveCardLabel(Guid cardId, Guid labelId)
    {
        var response = await _cardService.RemoveLabelAsync(cardId, labelId);
        return ToActionResult(response);
    }

    [HttpPut("{id}/cover")]
    public async Task<ActionResult<ApiResponse<bool>>> SetCardCover(Guid id, [FromBody] CoverDto coverDto)
    {
        var response = await _cardService.SetCoverAsync(id, coverDto);
        return ToActionResult(response);
    }

    [HttpPut("{id}/archived")]
    public async Task<ActionResult<ApiResponse<bool>>> SetCardArchived(Guid id, [FromBody] ArchivedDto archivedDto)
    {
        var response = await _cardService.SetArchivedAsync(id, archivedDto);
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteCard(Guid id)
    {
        var response = await _cardService.DeleteAsync(id);
        return ToActionResult(response);
    }

}
