using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateCheckItemDtoValidator : AbstractValidator<CreateCheckItemDto>
{
    public CreateCheckItemDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.CheckItemName);
        this.ValidPlacement();
        RuleFor(x => x.ChecklistId).Required();
    }
}

public sealed class UpdateCheckItemDtoValidator : AbstractValidator<UpdateCheckItemDto>
{
    public UpdateCheckItemDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.CheckItemName);
    }
}

public sealed class MoveCheckItemDtoValidator : AbstractValidator<MoveCheckItemDto>
{
    public MoveCheckItemDtoValidator()
    {
        RuleFor(x => x.ChecklistId).Required();
        this.ValidPlacement();
    }
}

public sealed class CheckedDtoValidator : AbstractValidator<CheckedDto>
{
    public CheckedDtoValidator()
    {
        RuleFor(x => x.Value).Required();
    }
}
