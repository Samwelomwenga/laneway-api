using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public static class Members
{
    public static Task<User?> FindUserAsync(this ApplicationDbContext context, UserLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lookup);

        return lookup switch
        {
            { UserId: { } id } => context.Users.FirstOrDefaultAsync(user => user.Id == id),
            { Username: { } username } => context.Users.FirstOrDefaultAsync(user => user.Username == username),
            _ => context.Users.FirstOrDefaultAsync(user => user.Email == lookup.Email)
        };
    }

    public static ApiResponse<T> UnknownUser<T>(UserLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        return ApiResponse<T>.ErrorResponse("A referenced resource does not exist", StatusCodes.Status400BadRequest,
            [new ApiError(lookup.Field, ErrorCodes.NotFound, $"No user has the {lookup.Field} '{lookup.Value}'.")]);
    }

    public static ApiResponse<T> AlreadyMember<T>(string place) =>
        ApiResponse<T>.ErrorResponse($"This user is already a member of this {place}", StatusCodes.Status409Conflict,
            [new ApiError(null, ErrorCodes.Duplicate, $"This user is already a member of this {place}.")]);

    public static ApiResponse<T> NotFound<T>() =>
        ApiResponse<T>.ErrorResponse("Member not found", StatusCodes.Status404NotFound);

    public static MemberUserDto UserOf(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new MemberUserDto(user.Id, user.Username, user.FirstName, user.LastName, user.ProfilePictureUrl);
    }
}
