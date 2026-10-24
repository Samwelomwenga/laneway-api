using FluentValidation;

namespace Laneway.Api;

public sealed class UpdateProfileDtoValidator : AbstractValidator<UpdateProfileDto>
{
    public UpdateProfileDtoValidator()
    {
        RuleFor(x => x.Username).Username();
        RuleFor(x => x.FirstName).Required().MaxLength(FieldLimits.UserFirstName);
        RuleFor(x => x.LastName).Required().MaxLength(FieldLimits.UserLastName);
        RuleFor(x => x.PhoneNumber).MaxLength(FieldLimits.UserPhoneNumber);
        RuleFor(x => x.Bio).MaxLength(FieldLimits.UserBio);
        RuleFor(x => x.Language).Required().MaxLength(FieldLimits.UserLanguage);
        RuleFor(x => x.TimeZone).Required().MaxLength(FieldLimits.UserTimeZone);
        RuleFor(x => x.Location).Required().MaxLength(FieldLimits.UserLocation);
    }
}
