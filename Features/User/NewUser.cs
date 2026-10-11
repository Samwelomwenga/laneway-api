namespace Laneway.Api;

public sealed record NewUser(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Bio,
    string Language,
    string TimeZone,
    string Location,
    Uri? ProfilePictureUrl)
{
    public static NewUser Of(CreateUserDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new NewUser(
            dto.Username,
            dto.Email,
            dto.FirstName,
            dto.LastName,
            dto.PhoneNumber,
            dto.Bio,
            dto.Language,
            dto.TimeZone,
            dto.Location,
            dto.ProfilePictureUrl);
    }
}
