using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/[controller]")]
public class CardController: ControllerBase
{
    private readonly ICardService _cardService;

    public CardController(ICardService cardService)
    {
        _cardService = cardService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CardDto>>>> GetCards([FromQuery] CardSearchDto searchDto)
    {
        var response = await _cardService.GetAllAsync(searchDto);
        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CardDto>>> GetCard(Guid id)
    {
        var response = await _cardService.GetByIdAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CardDto>>> CreateCard([FromBody] CreateCardDto createCardDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<CardDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _cardService.CreateAsync(createCardDto);
        if (!response.Success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetCard), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<CardDto>>> UpdateCard(Guid id, [FromBody] UpdateCardDto updateCardDto)
    {
        if (!ModelState.IsValid)
        {
           var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
           var errorResponse = ApiResponse<CardDto>.ErrorResponse("Invalid data", 400, errors);
              return BadRequest(errorResponse);
        }
        var response = await _cardService.UpdateAsync(id, updateCardDto);
        if (!response.Success)
        {
            return NotFound(response);
        }
        
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteCard(Guid id)
    {
        var response = await _cardService.DeleteAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
}
