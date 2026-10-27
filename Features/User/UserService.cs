using Microsoft.EntityFrameworkCore;

namespace Laneway.Api
{
    public interface IUserService
    {
        Task<ApiResponse<UserDto>> GetProfileAsync();
        Task<ApiResponse<UserDto>> UpdateProfileAsync(UpdateProfileDto updateProfileDto);
        Task<ApiResponse<UserSummaryDto>> GetSummaryAsync(Guid id);
    }

    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly Actor _actor;
        private readonly ILogger _log;

        public UserService(ApplicationDbContext context, Actor actor, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(loggers);

            _context = context;
            _actor = actor;
            _log = loggers.CreateLogger(AuthLog.Category);
        }

        public async Task<ApiResponse<UserDto>> GetProfileAsync()
        {
            var user = await ProfileAsync();
            if (user is null)
            {
                return ApiResponse<UserDto>.ErrorResponse("User not found", 404);
            }
            return ApiResponse<UserDto>.SuccessResponse(UserView.Of(user), "Profile retrieved successfully", 200);
        }

        public async Task<ApiResponse<UserDto>> UpdateProfileAsync(UpdateProfileDto updateProfileDto)
        {
            var user = await ProfileAsync();
            if (user is null)
            {
                return ApiResponse<UserDto>.ErrorResponse("User not found", 404);
            }

            var errors = await UserRules.TakenAsync(_context, updateProfileDto.Username, email: null, exceptId: user.Id);
            if (errors.Count > 0)
            {
                return UserRules.AlreadyExists<UserDto>(errors);
            }

            var previousUsername = user.Username;
            user.Apply(updateProfileDto);
            _context.StampChange(user, _actor);

            await _context.SaveChangesAsync();
            if (user.Username != previousUsername)
            {
                _log.UsernameChanged(user.Id);
            }

            return ApiResponse<UserDto>.SuccessResponse(UserView.Of(user), "Profile updated successfully", 200);
        }

        public async Task<ApiResponse<UserSummaryDto>> GetSummaryAsync(Guid id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
            {
                return ApiResponse<UserSummaryDto>.ErrorResponse("User not found", 404);
            }
            return ApiResponse<UserSummaryDto>.SuccessResponse(
                UserView.SummaryOf(user), "User retrieved successfully", 200);
        }

        private Task<User?> ProfileAsync() =>
            _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == _actor.Id);
    }
}
