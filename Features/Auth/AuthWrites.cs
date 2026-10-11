namespace Laneway.Api;

public sealed record SignUpWrite(NewUser User, string Password)
{
    public static SignUpWrite Of(SignUpDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new SignUpWrite(
            new NewUser(
                Writes.Required(dto.Username, "username"),
                Writes.Required(dto.Email, "email"),
                Writes.Required(dto.FirstName, "firstName"),
                Writes.Required(dto.LastName, "lastName"),
                dto.PhoneNumber,
                dto.Bio,
                Writes.Required(dto.Language, "language"),
                Writes.Required(dto.TimeZone, "timeZone"),
                Writes.Required(dto.Location, "location"),
                dto.ProfilePictureUrl),
            Writes.Required(dto.Password, "password"));
    }
}

public sealed record SignInWrite(string Email, string Password)
{
    public static SignInWrite Of(SignInDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new SignInWrite(Writes.Required(dto.Email, "email"), Writes.Required(dto.Password, "password"));
    }
}
