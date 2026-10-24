using FluentValidation;

namespace Laneway.Api;

public sealed class SignUpDtoValidator : AbstractValidator<SignUpDto>
{
    public SignUpDtoValidator()
    {
        RuleFor(x => x.Username).Username();
        RuleFor(x => x.Email).Required().MaxLength(FieldLimits.UserEmail);
        RuleFor(x => x.Password).NewPassword();
        RuleFor(x => x.FirstName).Required().MaxLength(FieldLimits.UserFirstName);
        RuleFor(x => x.LastName).Required().MaxLength(FieldLimits.UserLastName);
        RuleFor(x => x.PhoneNumber).MaxLength(FieldLimits.UserPhoneNumber);
        RuleFor(x => x.Bio).MaxLength(FieldLimits.UserBio);
        RuleFor(x => x.Language).Required().MaxLength(FieldLimits.UserLanguage);
        RuleFor(x => x.TimeZone).Required().MaxLength(FieldLimits.UserTimeZone);
        RuleFor(x => x.Location).Required().MaxLength(FieldLimits.UserLocation);
    }
}

public sealed class VerifyEmailDtoValidator : AbstractValidator<VerifyEmailDto>
{
    public VerifyEmailDtoValidator()
    {
        RuleFor(x => x.Token).Required();
    }
}

public sealed class SignInDtoValidator : AbstractValidator<SignInDto>
{
    public SignInDtoValidator()
    {
        RuleFor(x => x.Email).Required();
        RuleFor(x => x.Password).Required().PasswordWithinMaximum();
    }
}

public sealed class ChangePasswordDtoValidator : AbstractValidator<ChangePasswordDto>
{
    public ChangePasswordDtoValidator()
    {
        RuleFor(x => x.CurrentPassword).Required().PasswordWithinMaximum();
        RuleFor(x => x.NewPassword).NewPassword();
    }
}

public sealed class ChangeEmailDtoValidator : AbstractValidator<ChangeEmailDto>
{
    public ChangeEmailDtoValidator()
    {
        RuleFor(x => x.Password).Required().PasswordWithinMaximum();
        RuleFor(x => x.NewEmail).Required().MaxLength(FieldLimits.UserEmail);
    }
}

public sealed class DeleteUserDtoValidator : AbstractValidator<DeleteUserDto>
{
    public DeleteUserDtoValidator()
    {
        RuleFor(x => x.Password).Required().PasswordWithinMaximum();
    }
}
