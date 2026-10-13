using FluentValidation;

namespace Laneway.Api;

public sealed class UpdateProfileDtoValidator : AbstractValidator<UpdateProfileDto>
{
    public UpdateProfileDtoValidator()
    {
        RuleFor(x => x.Username).Required();
        RuleFor(x => x.FirstName).Required();
        RuleFor(x => x.LastName).Required();
        RuleFor(x => x.Language).Required();
        RuleFor(x => x.TimeZone).Required();
        RuleFor(x => x.Location).Required();
    }
}
