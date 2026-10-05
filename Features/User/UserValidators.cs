using FluentValidation;

namespace Laneway.Api;

public sealed class CreateUserDtoValidator : AbstractValidator<CreateUserDto>
{
    public CreateUserDtoValidator()
    {
        RuleFor(x => x.Username).Required();
        RuleFor(x => x.Email).Required();
        RuleFor(x => x.FirstName).Required();
        RuleFor(x => x.LastName).Required();
        RuleFor(x => x.Language).Required();
        RuleFor(x => x.TimeZone).Required();
        RuleFor(x => x.Location).Required();
    }
}

public sealed class UpdateUserDtoValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserDtoValidator()
    {
        RuleFor(x => x.Username).Required();
        RuleFor(x => x.Email).Required();
        RuleFor(x => x.FirstName).Required();
        RuleFor(x => x.LastName).Required();
        RuleFor(x => x.Language).Required();
        RuleFor(x => x.TimeZone).Required();
        RuleFor(x => x.Location).Required();
    }
}
