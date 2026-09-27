using FluentValidation;

namespace DefaultNamespace;

public sealed class UpdateAttachmentDtoValidator : AbstractValidator<UpdateAttachmentDto>
{
    public UpdateAttachmentDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.AttachmentName);
    }
}
