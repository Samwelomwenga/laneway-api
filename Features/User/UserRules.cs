using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public static class UserRules
{
    public static async Task<List<ApiError>> TakenAsync(
        ApplicationDbContext context, string username, string? email, Guid? exceptId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var takenUsers = await context.Users
            .Where(u => u.Id != exceptId && (u.Username == username || u.Email == email))
            .Select(u => new { u.Username, u.Email })
            .ToListAsync();
        List<ApiError> errors = [];
        if (takenUsers.Any(u => u.Username == username))
        {
            errors.Add(new ApiError("username", ErrorCodes.Duplicate, "A user with this username already exists."));
        }
        if (email is not null && takenUsers.Any(u => u.Email == email))
        {
            errors.Add(new ApiError("email", ErrorCodes.Duplicate, "A user with this email already exists."));
        }
        return errors;
    }

    public static ApiResponse<T> AlreadyExists<T>(List<ApiError> errors) =>
        ApiResponse<T>.ErrorResponse("User already exists", 409, errors);

    public static void Apply(this User user, UpdateProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(profile);

        user.Username = profile.Username;
        user.FirstName = profile.FirstName;
        user.LastName = profile.LastName;
        user.PhoneNumber = profile.PhoneNumber;
        user.Bio = profile.Bio;
        user.Language = profile.Language;
        user.TimeZone = profile.TimeZone;
        user.Location = profile.Location;
        user.ProfilePictureUrl = profile.ProfilePictureUrl;
    }

    public static void Replace(this User user, NewUser fields, string passwordHash, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(fields);

        user.Username = fields.Username;
        user.PasswordHash = passwordHash;
        user.FirstName = fields.FirstName;
        user.LastName = fields.LastName;
        user.PhoneNumber = fields.PhoneNumber;
        user.Bio = fields.Bio;
        user.Language = fields.Language;
        user.TimeZone = fields.TimeZone;
        user.Location = fields.Location;
        user.ProfilePictureUrl = fields.ProfilePictureUrl;
        user.UpdatedAt = now;
        user.UpdatedBy = user.Id;
    }

    public static User Create(NewUser fields, string passwordHash, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            Username = fields.Username,
            Email = fields.Email,
            PasswordHash = passwordHash,
            FirstName = fields.FirstName,
            LastName = fields.LastName,
            PhoneNumber = fields.PhoneNumber,
            Bio = fields.Bio,
            Language = fields.Language,
            TimeZone = fields.TimeZone,
            Location = fields.Location,
            ProfilePictureUrl = fields.ProfilePictureUrl,
            CreatedAt = now,
            CreatedBy = id
        };
        user.Accounts =
        [
            new Account
            {
                Id = Guid.NewGuid(),
                Name = $"{user.Username}'s Account",
                UserId = id,
                CreatedAt = now,
                CreatedBy = id
            }
        ];
        return user;
    }
}
