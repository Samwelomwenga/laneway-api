using FluentValidation;

namespace Laneway.Api;

public sealed class CreateChecklistDtoValidator : AbstractValidator<CreateChecklistDto>
{
    public CreateChecklistDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.ChecklistName);
        this.ValidPlacement();
        RuleFor(x => x.CardId).Required();
    }
}

public sealed class UpdateChecklistDtoValidator : AbstractValidator<UpdateChecklistDto>
{
    public UpdateChecklistDtoValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(FieldLimits.ChecklistName);
    }
}

public sealed class ReorderChecklistDtoValidator : AbstractValidator<ReorderChecklistDto>
{
    public ReorderChecklistDtoValidator()
    {
        this.ValidPlacement();
    }
}
