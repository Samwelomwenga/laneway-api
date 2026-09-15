using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateLabelDtoValidator : AbstractValidator<CreateLabelDto>
{
    public CreateLabelDtoValidator()
    {
        RuleFor(x => x.Name).Required();
    }
}

public sealed class UpdateLabelDtoValidator : AbstractValidator<UpdateLabelDto>
{
    public UpdateLabelDtoValidator()
    {
        RuleFor(x => x.Name).Required();
    }
}
