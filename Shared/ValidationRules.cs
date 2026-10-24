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

    public static IRuleBuilderOptions<T, string?> Username<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Required()
            .MaxLength(FieldLimits.Username)
            .Must(username => string.IsNullOrEmpty(username) || username.Length > FieldLimits.Username || Usernames.IsValid(username))
            .WithErrorCode(ErrorCodes.Invalid)
            .WithMessage($"'{{PropertyName}}' must be {Usernames.MinimumLength} or more of a-z, 0-9, '_' and '-', starting with a letter or digit.");

    public static IRuleBuilderOptions<T, int?> NotNegative<T>(this IRuleBuilder<T, int?> rule) =>
        rule.GreaterThanOrEqualTo(0)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage("'{PropertyName}' can't be negative.");

    public static IRuleBuilderOptions<T, Guid?> NotNullId<T>(this IRuleBuilder<T, Guid?> rule) =>
        rule.NotNull()
            .WithErrorCode(ErrorCodes.InvalidFormat)
            .WithMessage("'{PropertyName}[{CollectionIndex}]' isn't in a valid format.");

    public static IRuleBuilderOptions<T, string?> NewPassword<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Required()
            .Must(password => string.IsNullOrEmpty(password) || Passwords.Length(password) >= Passwords.MinimumLength)
            .WithErrorCode(ErrorCodes.TooShort)
            .WithMessage($"'{{PropertyName}}' must be at least {Passwords.MinimumLength} characters.")
            .PasswordWithinMaximum();

    public static IRuleBuilderOptions<T, string?> PasswordWithinMaximum<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(password => password is null || Passwords.Length(password) <= Passwords.MaximumLength)
            .WithErrorCode(ErrorCodes.TooLong)
            .WithMessage($"'{{PropertyName}}' must be {Passwords.MaximumLength} characters or fewer.");
}
