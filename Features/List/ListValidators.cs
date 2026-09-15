using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateListDtoValidator : AbstractValidator<CreateListDto>
{
    public CreateListDtoValidator()
    {
        RuleFor(x => x.Name).Required();
    }
}

public sealed class UpdateListDtoValidator : AbstractValidator<UpdateListDto>
{
    public UpdateListDtoValidator()
    {
        RuleFor(x => x.Name).Required();
    }
}
