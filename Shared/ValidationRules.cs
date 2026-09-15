using FluentValidation;

namespace DefaultNamespace;

public static class ValidationRules
{
    public static IRuleBuilderOptions<T, TProperty> Required<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.NotEmpty()
            .WithErrorCode(ErrorCodes.Required)
            .WithMessage("'{PropertyName}' is required.");

    public static IRuleBuilderOptions<T, string?> MaxLength<T>(this IRuleBuilder<T, string?> rule, int maxLength) =>
        rule.MaximumLength(maxLength)
            .WithErrorCode(ErrorCodes.TooLong)
            .WithMessage($"'{{PropertyName}}' must be {maxLength} characters or fewer.");
}
