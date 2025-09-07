using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace
{
    public interface IAccountService
    {
        Task<PagedResponse<AccountDto>> GetAllAsync(AccountSearchDto searchDto);
        Task<ApiResponse<AccountDto>> GetByIdAsync(Guid id);
        Task<ApiResponse<AccountDto>> CreateAsync(CreateAccountDto createAccountDto);
        Task<ApiResponse<AccountDto>> UpdateAsync(Guid id, UpdateAccountDto updateAccountDto);
        Task<ApiResponse<bool>> DeleteAsync(Guid id);
    }

    public class AccountService : IAccountService
    {
        private readonly ApplicationDbContext _context;

        public AccountService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResponse<AccountDto>> GetAllAsync(AccountSearchDto searchDto)
        {
            try
            {
                var query = _context.Accounts.AsQueryable();

                if (!string.IsNullOrEmpty(searchDto.SearchTerm))
                {
                    query = query.Where(a => a.Name.Contains(searchDto.SearchTerm));
                }

                if (searchDto.UserId.HasValue)
                {
                    query = query.Where(a => a.UserId == searchDto.UserId.Value);
                }

                var totalCount = await query.CountAsync();
                var accounts = await query
                    .OrderBy(a => a.CreatedAt)
                    .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                    .Take(searchDto.PageSize)
                    .Include(a => a.User)
                    .ToListAsync();

                var accountDtos = accounts.Select(MapToDto).ToList();

                return PagedResponse<AccountDto>.SuccessResponse(
                    accountDtos,
                    totalCount,
                    searchDto.PageSize,
                    searchDto.PageNumber,
                    "Accounts retrieved successfully"
                );
            }
            catch (Exception e)
            {
                return PagedResponse<AccountDto>.ErrorResponse("An error occurred while retrieving accounts", 500,
                    new List<string> { e.Message });
            }
        }

        public async Task<ApiResponse<AccountDto>> GetByIdAsync(Guid id)
        {
            try
            {
                var account = await _context.Accounts
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.Id == id);
                if (account == null)
                    return ApiResponse<AccountDto>.ErrorResponse("Account not found", 404);
                return ApiResponse<AccountDto>.SuccessResponse(MapToDto(account), "Account retrieved successfully", 200);
            }
            catch (Exception e)
            {
                return ApiResponse<AccountDto>.ErrorResponse("An error occurred while retrieving the account", 500,
                    new List<string> { e.Message });
            }
        }

        public async Task<ApiResponse<AccountDto>> CreateAsync(CreateAccountDto createAccountDto)
        {
            try
            {
                var account = MapToEntity(createAccountDto);
                _context.Accounts.Add(account);
                await _context.SaveChangesAsync();
                var newAccount = await _context.Accounts.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == account.Id);
                return ApiResponse<AccountDto>.SuccessResponse(MapToDto(newAccount!), "Account created successfully", 201);
            }
            catch (Exception e)
            {
                return ApiResponse<AccountDto>.ErrorResponse("An error occurred while creating the account", 500,
                    new List<string> { e.Message });
            }
        }

        public async Task<ApiResponse<AccountDto>> UpdateAsync(Guid id, UpdateAccountDto updateAccountDto)
        {
            try
            {
                var account = await _context.Accounts.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == id);
                if (account == null)
                    return ApiResponse<AccountDto>.ErrorResponse("Account not found", 404);
                account.Name = updateAccountDto.Name;
                account.UpdatedAt = DateTime.UtcNow;
                account.UpdatedBy = updateAccountDto.UpdatedBy;
                _context.Accounts.Update(account);
                await _context.SaveChangesAsync();
                return ApiResponse<AccountDto>.SuccessResponse(MapToDto(account), "Account updated successfully", 200);
            }
            catch (Exception e)
            {
                return ApiResponse<AccountDto>.ErrorResponse("An error occurred while updating the account", 500,
                    new List<string> { e.Message });
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
        {
            try
            {
                var account = await _context.Accounts.FindAsync(id);
                if (account == null)
                    return ApiResponse<bool>.ErrorResponse("Account not found", 404);
                _context.Accounts.Remove(account);
                await _context.SaveChangesAsync();
                return ApiResponse<bool>.SuccessResponse(true, "Account deleted successfully", 200);
            }
            catch (Exception e)
            {
                return ApiResponse<bool>.ErrorResponse("An error occurred while deleting the account", 500,
                    new List<string> { e.Message });
            }
        }

        private static AccountDto MapToDto(Account account)
        {
            var userDto = account.User == null ? null : new UserDto(
                account.User.Id,
                account.User.Username,
                account.User.Email,
                account.User.FirstName,
                account.User.LastName,
                account.User.PhoneNumber,
                account.User.Bio,
                account.User.Language,
                account.User.TimeZone,
                account.User.Location,
                account.User.ProfilePictureUrl,
                account.User.IsActive,
                new List<AccountDto>(), // Prevent circular reference
                account.User.CreatedAt,
                account.User.UpdatedAt,
                account.User.CreatedBy,
                account.User.UpdatedBy
            );
            return new AccountDto(
                account.Id,
                account.Name,
                account.CreatedBy,
                account.CreatedAt,
                account.UpdatedAt,
                account.UpdatedBy,
                userDto!
            );
        }

        private static Account MapToEntity(CreateAccountDto dto)
        {
            return new Account
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                UserId = dto.UserId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = dto.CreatedBy
            };
        }
    }
}
