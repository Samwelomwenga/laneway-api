using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateListDtoValidator : AbstractValidator<CreateListDto>
{
    public CreateListDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.ListName);
        this.ValidPlacement();
        RuleFor(x => x.BoardId).Required();
    }
}

public sealed class UpdateListDtoValidator : AbstractValidator<UpdateListDto>
{
    public UpdateListDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.ListName);
    }
}

public sealed class MoveListDtoValidator : AbstractValidator<MoveListDto>
{
    public MoveListDtoValidator()
    {
        RuleFor(x => x.BoardId).Required();
        this.ValidPlacement();
    }
}
