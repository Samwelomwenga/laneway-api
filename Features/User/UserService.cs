using Microsoft.EntityFrameworkCore;

namespace Laneway.Api
{
    public interface IUserService
    {
        Task<PagedResponse<UserDto>> GetAllAsync(UserSearchDto searchDto);
        Task<ApiResponse<UserDto>> GetByIdAsync(Guid id);
        Task<ApiResponse<UserDto>> CreateAsync(CreateUserDto createUserDto);
        Task<ApiResponse<UserDto>> UpdateAsync(Guid id, UpdateUserDto updateUserDto);
        Task<ApiResponse<bool>> DeleteAsync(Guid id);
        Task<ApiResponse<bool>> UserNameExistsAsync(string username);
    }

    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly Actor _actor;

        public UserService(ApplicationDbContext context, Actor actor)
        {
            _context = context;
            _actor = actor;
        }

        public async Task<PagedResponse<UserDto>> GetAllAsync(UserSearchDto searchDto)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(searchDto.SearchTerm))
            {
                query = query.Where(u => u.Username.Contains(searchDto.SearchTerm) ||
                                        u.Email.Contains(searchDto.SearchTerm) ||
                                        u.FirstName.Contains(searchDto.SearchTerm) ||
                                        u.LastName.Contains(searchDto.SearchTerm));

            }

            var totalCount = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                .Take(searchDto.PageSize)
                .Include(u => u.Accounts)
                .ToListAsync();

            var userDtos = users.Select(UserView.Of).ToList();

            return PagedResponse<UserDto>.SuccessResponse(
                userDtos,
                totalCount,
                searchDto.PageSize,
                searchDto.PageNumber,
                "Users retrieved successfully"
            );
        }

        public async Task<ApiResponse<UserDto>> GetByIdAsync(Guid id)
        {
            var existingUser = await _context.Users
                .Include(u => u.Accounts)
                .Where(u => u.Id == id)
                .FirstOrDefaultAsync();
            if (existingUser is null)
            {
                return ApiResponse<UserDto>.ErrorResponse("User not found", 404);
            }
            var userDto = UserView.Of(existingUser);
            return ApiResponse<UserDto>.SuccessResponse(userDto, "User retrieved successfully", 200);
        }

        public async Task<ApiResponse<UserDto>> CreateAsync(CreateUserDto createUserDto)
        {
            var errors = await UserRules.TakenAsync(_context, createUserDto.Username, createUserDto.Email, exceptId: null);
            if (errors.Count > 0)
            {
                return UserRules.AlreadyExists<UserDto>(errors);
            }
            var newUser = UserRules.Create(NewUser.Of(createUserDto), "temp_hash", DateTime.UtcNow);
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();
            return ApiResponse<UserDto>.SuccessResponse(UserView.Of(newUser), "User created successfully", 201);
        }

        public async Task<ApiResponse<UserDto>> UpdateAsync(Guid id, UpdateUserDto updateUserDto)
        {
            var existingUser = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == id);
            if (existingUser is null)
            {
                return ApiResponse<UserDto>.ErrorResponse("User not found", 404);
            }

            var errors = await UserRules.TakenAsync(_context, updateUserDto.Username, updateUserDto.Email, exceptId: id);
            if (errors.Count > 0)
            {
                return UserRules.AlreadyExists<UserDto>(errors);
            }

            existingUser.Username = updateUserDto.Username;
            existingUser.Email = updateUserDto.Email;
            existingUser.FirstName = updateUserDto.FirstName;
            existingUser.LastName = updateUserDto.LastName;
            existingUser.PhoneNumber = updateUserDto.PhoneNumber;
            existingUser.Bio = updateUserDto.Bio;
            existingUser.Language = updateUserDto.Language;
            existingUser.TimeZone = updateUserDto.TimeZone;
            existingUser.Location = updateUserDto.Location;
            existingUser.ProfilePictureUrl = updateUserDto.ProfilePictureUrl;
            _context.StampChange(existingUser, _actor);

            await _context.SaveChangesAsync();

            return ApiResponse<UserDto>.SuccessResponse(UserView.Of(existingUser), "User updated successfully", 200);
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return ApiResponse<bool>.ErrorResponse("User not found", 404);
            }

            user.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "User deleted successfully", 204);
        }
        public async Task<ApiResponse<bool>> UserNameExistsAsync(string username)
        {
            var exists = await _context.Users.AnyAsync(u => u.Username == username);
            return ApiResponse<bool>.SuccessResponse(exists, "Username existence checked successfully", 200);
        }
    }
}
