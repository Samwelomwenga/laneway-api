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
        RuleFor(x => x.Visibility).Required();
    }
}

public sealed class MoveBoardDtoValidator : AbstractValidator<MoveBoardDto>
{
    public MoveBoardDtoValidator()
    {
        RuleFor(x => x.WorkspaceId).Required();
    }
}

public sealed class CopyBoardDtoValidator : AbstractValidator<CopyBoardDto>
{
    public CopyBoardDtoValidator()
    {
        RuleFor(x => x.WorkspaceId).Required();
        RuleFor(x => x.Name).MaxLength(FieldLimits.BoardName);
        RuleForEach(x => x.Keep)
            .Must((request, part) =>
                !BoardCopyKeep.NeedsCards(part) || request.Keep!.Contains(BoardCopyPart.Cards))
            .WithErrorCode(ErrorCodes.RequiresCards)
            .WithMessage("'{PropertyName}[{CollectionIndex}]' needs 'cards' in 'keep' too.");
    }
}
