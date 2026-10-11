namespace Laneway.Api;

public static class UserView
{
    public static UserDto Of(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

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
            user.Accounts?.Select(AccountView.Of).ToList() ?? [],
            user.CreatedAt,
            user.UpdatedAt,
            user.CreatedBy,
            user.UpdatedBy
        );
    }
}
