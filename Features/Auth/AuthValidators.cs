using FluentValidation;

namespace Laneway.Api;

public sealed class SignUpDtoValidator : AbstractValidator<SignUpDto>
{
    public SignUpDtoValidator()
    {
        RuleFor(x => x.Username).Required();
        RuleFor(x => x.Email).Required();
        RuleFor(x => x.Password).NewPassword();
        RuleFor(x => x.FirstName).Required();
        RuleFor(x => x.LastName).Required();
        RuleFor(x => x.Language).Required();
        RuleFor(x => x.TimeZone).Required();
        RuleFor(x => x.Location).Required();
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
