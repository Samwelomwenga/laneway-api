using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateListDtoValidator : AbstractValidator<CreateListDto>
{
    public CreateListDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.ListName);
        RuleFor(x => x.Position).ValidPosition();
        RuleFor(x => x).OnePlacement(x => new Placement(x.Position, x.Before, x.After));
        RuleFor(x => x.BoardId).Required();
        RuleFor(x => x.IsArchived).Required();
    }
}

public sealed class UpdateListDtoValidator : AbstractValidator<UpdateListDto>
{
    public UpdateListDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.ListName);
        RuleFor(x => x.Position).Required();
        RuleFor(x => x.BoardId).Required();
        RuleFor(x => x.IsArchived).Required();
    }
}
