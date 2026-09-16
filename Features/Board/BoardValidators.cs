using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateBoardDtoValidator : AbstractValidator<CreateBoardDto>
{
    public CreateBoardDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.BoardName);
        RuleFor(x => x.Description).MaxLength(FieldLimits.BoardDescription);
        RuleFor(x => x.WorkspaceId).Required();
        RuleFor(x => x.Visibility).Required();
    }
}

public sealed class UpdateBoardDtoValidator : AbstractValidator<UpdateBoardDto>
{
    public UpdateBoardDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.BoardName);
        RuleFor(x => x.Description).MaxLength(FieldLimits.BoardDescription);
        RuleFor(x => x.WorkspaceId).Required();
        RuleFor(x => x.Visibility).Required();
    }
}
