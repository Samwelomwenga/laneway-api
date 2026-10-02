using FluentValidation;

namespace Laneway.Api;

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

    public static IRuleBuilderOptions<T, int?> NotNegative<T>(this IRuleBuilder<T, int?> rule) =>
        rule.GreaterThanOrEqualTo(0)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage("'{PropertyName}' can't be negative.");

    public static IRuleBuilderOptions<T, Guid?> NotNullId<T>(this IRuleBuilder<T, Guid?> rule) =>
        rule.NotNull()
            .WithErrorCode(ErrorCodes.InvalidFormat)
            .WithMessage("'{PropertyName}[{CollectionIndex}]' isn't in a valid format.");
}
