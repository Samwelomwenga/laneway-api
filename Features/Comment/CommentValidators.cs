using FluentValidation;

namespace Laneway.Api;

public sealed class CommentTextDtoValidator : AbstractValidator<CommentTextDto>
{
    public CommentTextDtoValidator()
    {
        RuleFor(x => x.Text).Required().MaxLength(FieldLimits.CommentText);
    }
}
