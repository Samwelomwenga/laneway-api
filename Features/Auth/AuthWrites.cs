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

public sealed record ChangePasswordWrite(string CurrentPassword, string NewPassword)
{
    public static ChangePasswordWrite Of(ChangePasswordDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new ChangePasswordWrite(
            Writes.Required(dto.CurrentPassword, "currentPassword"), Writes.Required(dto.NewPassword, "newPassword"));
    }
}

public sealed record ChangeEmailWrite(string Password, string NewEmail)
{
    public static ChangeEmailWrite Of(ChangeEmailDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new ChangeEmailWrite(Writes.Required(dto.Password, "password"), Writes.Required(dto.NewEmail, "newEmail"));
    }
}

public sealed record DeleteUserWrite(string Password)
{
    public static DeleteUserWrite Of(DeleteUserDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new DeleteUserWrite(Writes.Required(dto.Password, "password"));
    }
}
