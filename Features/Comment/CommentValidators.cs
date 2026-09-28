using FluentValidation;

namespace DefaultNamespace;

public sealed class CommentTextDtoValidator : AbstractValidator<CommentTextDto>
{
    public CommentTextDtoValidator()
    {
        RuleFor(x => x.Text).Required().MaxLength(FieldLimits.CommentText);
    }
}
