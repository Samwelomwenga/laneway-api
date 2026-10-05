using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace
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

        public UserService(ApplicationDbContext context)
        {
            _context = context;
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


            if (searchDto.IsActive.HasValue)
            {
                query = query.Where(u => u.IsActive == searchDto.IsActive.Value);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);
            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                .Take(searchDto.PageSize)
                .Include(u => u.Accounts)
                .ToListAsync();

            var userDtos = users.Select(MapToDto).ToList();

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
                return ApiResponse<UserDto>.ErrorResponse("User not found", 404, ["User with the specified ID does not exist."]);
            }
            var userDto = MapToDto(existingUser);
            return ApiResponse<UserDto>.SuccessResponse(userDto, "User retrieved successfully", 200);
        }

        public async Task<ApiResponse<UserDto>> CreateAsync(CreateUserDto createUserDto)
        {
            var exists = await _context.Users.AnyAsync(u => u.Username == createUserDto.Username || u.Email == createUserDto.Email);
            if (exists)
            {
                return ApiResponse<UserDto>.ErrorResponse("User already exists", 409, ["A user with the same username or email already exists."]);
            }
            var newUser = MapToEntity(createUserDto);
            _context.Users.Add(newUser);

            var newAccount = new Account
            {
                Id = Guid.NewGuid(),
                Name = $"{newUser.Username}'s Account",
                UserId = newUser.Id,
                CreatedAt = newUser.CreatedAt,
                CreatedBy = newUser.CreatedBy
            };
            _context.Accounts.Add(newAccount);
            await _context.SaveChangesAsync();
            return ApiResponse<UserDto>.SuccessResponse(MapToDto(newUser), "User created successfully", 201);
        }

        public async Task<ApiResponse<UserDto>> UpdateAsync(Guid id, UpdateUserDto updateUserDto)
        {
            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser is null)
            {
                return ApiResponse<UserDto>.ErrorResponse("User not found", 404, ["User with the specified ID does not exist."]);
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
            existingUser.IsActive = updateUserDto.IsActive;
            existingUser.UpdatedAt = DateTime.UtcNow;
            existingUser.UpdatedBy = Guid.NewGuid();

            _context.Users.Update(existingUser);
            await _context.SaveChangesAsync();

            return ApiResponse<UserDto>.SuccessResponse(MapToDto(existingUser), "User updated successfully", 200);
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return ApiResponse<bool>.ErrorResponse("User not found", 404, ["User with the specified ID does not exist."]);
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "User deleted successfully", 204);
        }
        public async Task<ApiResponse<bool>> UserNameExistsAsync(string username)
        {
            var exists = await _context.Users.AnyAsync(u => u.Username == username);
            return ApiResponse<bool>.SuccessResponse(exists, "Username existence checked successfully", 200);
        }

        private static UserDto MapToDto(User user)
        {
            var accounts = user.Accounts?.Select(a => new AccountDto(a.Id, a.Name, a.CreatedBy, a.CreatedAt, a.UpdatedAt, a.UpdatedBy, null!)).ToList() ?? [];
            return new UserDto
            (
                user.Id,
                user.Username,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.Bio,
                user.Language,
                user.TimeZone,
                user.Location,
                user.ProfilePictureUrl,
                user.IsActive,
                accounts,
                user.CreatedAt,
                user.UpdatedAt,
                user.CreatedBy,
                user.UpdatedBy
            );
        }

        private static User MapToEntity(CreateUserDto createUserDto)
        {
            return new User
            {
                Id = Guid.NewGuid(),
                Username = createUserDto.Username,
                Email = createUserDto.Email,
                FirstName = createUserDto.FirstName,
                LastName = createUserDto.LastName,
                PhoneNumber = createUserDto.PhoneNumber,
                Bio = createUserDto.Bio,
                Language = createUserDto.Language,
                TimeZone = createUserDto.TimeZone,
                Location = createUserDto.Location,
                ProfilePictureUrl = createUserDto.ProfilePictureUrl,
                PasswordHash = "temp_hash",
                IsActive = createUserDto.IsActive,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = Guid.NewGuid()
            };
        }
    }
}
