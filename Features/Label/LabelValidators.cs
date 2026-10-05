using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateLabelDtoValidator : AbstractValidator<CreateLabelDto>
{
    public CreateLabelDtoValidator()
    {
        RuleFor(x => x.Name).MaxLength(FieldLimits.LabelName).NameOrColor(x => x.Color);
        RuleFor(x => x.BoardId).Required();
    }
}

public sealed class UpdateLabelDtoValidator : AbstractValidator<UpdateLabelDto>
{
    public UpdateLabelDtoValidator()
    {
        RuleFor(x => x.Name).MaxLength(FieldLimits.LabelName).NameOrColor(x => x.Color);
    }
}

internal static class LabelRules
{
    public static IRuleBuilderOptions<T, string?> NameOrColor<T>(
        this IRuleBuilder<T, string?> rule, Func<T, Color?> color) =>
        rule.Must((label, name) => !string.IsNullOrWhiteSpace(name) || color(label) is not null)
            .WithErrorCode(ErrorCodes.Required)
            .WithMessage("A label with no color needs a name.");
}
