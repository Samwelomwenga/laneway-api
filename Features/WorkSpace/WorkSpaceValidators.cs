using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateWorkSpaceDtoValidator : AbstractValidator<CreateWorkSpaceDto>
{
    public CreateWorkSpaceDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(100);
        RuleFor(x => x.Description).MaxLength(1000);
        RuleFor(x => x.Visibility).Required();
        RuleFor(x => x.IsArchived).Required();
    }
}

public sealed class UpdateWorkSpaceDtoValidator : AbstractValidator<UpdateWorkSpaceDto>
{
    public UpdateWorkSpaceDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(100);
        RuleFor(x => x.Description).MaxLength(1000);
        RuleFor(x => x.Visibility).Required();
        RuleFor(x => x.IsArchived).Required();
    }
}
