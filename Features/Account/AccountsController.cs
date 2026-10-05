using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/accounts")]
public class AccountsController : ApiControllerBase
{
    private readonly IAccountService _accountService;

    public AccountsController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<AccountDto>>> GetAccounts([FromQuery] AccountSearchDto searchDto)
    {
        var result = await _accountService.GetAllAsync(searchDto);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> GetAccount(Guid id)
    {
        var result = await _accountService.GetByIdAsync(id);
        return ToActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AccountDto>>> CreateAccount([FromBody] CreateAccountDto createAccountDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<AccountDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _accountService.CreateAsync(createAccountDto);
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetAccount), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> UpdateAccount(Guid id, [FromBody] UpdateAccountDto updateAccountDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<AccountDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _accountService.UpdateAsync(id, updateAccountDto);
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAccount(Guid id)
    {
        var result = await _accountService.DeleteAsync(id);
        return ToActionResult(result);
    }
}
