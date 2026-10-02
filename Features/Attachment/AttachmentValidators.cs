using FluentValidation;

namespace Laneway.Api;

public sealed class CreateLinkAttachmentDtoValidator : AbstractValidator<CreateLinkAttachmentDto>
{
    public CreateLinkAttachmentDtoValidator()
    {
        RuleFor(x => x.Url).Required().MaxLength(FieldLimits.AttachmentUrl).WebAddress();
        RuleFor(x => x.Name).MaxLength(FieldLimits.AttachmentName);
    }
}

public sealed class UpdateAttachmentDtoValidator : AbstractValidator<UpdateAttachmentDto>
{
    public UpdateAttachmentDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.AttachmentName);
    }
}

internal static class AttachmentRules
{
    public static IRuleBuilderOptions<T, string?> WebAddress<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(url => string.IsNullOrEmpty(url) || IsWebAddress(url))
            .WithErrorCode(ErrorCodes.InvalidFormat)
            .WithMessage("'{PropertyName}' must be an http or https address.");

    private static bool IsWebAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}
