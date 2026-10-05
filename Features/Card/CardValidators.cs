using FluentValidation;

namespace Laneway.Api;

public sealed class CreateCardDtoValidator : AbstractValidator<CreateCardDto>
{
    public CreateCardDtoValidator()
    {
        RuleFor(x => x.Title).Required().MaxLength(FieldLimits.CardTitle);
        RuleFor(x => x.Description).MaxLength(FieldLimits.CardDescription);
        this.ValidPlacement();
        RuleFor(x => x.ListId).Required();
        RuleFor(x => x.IsDueComplete).Required().CompleteNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.DueReminderMinutes).NotNegative().ReminderNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.StartDate).NotAfterDueDate(x => x.DueDate);
        RuleForEach(x => x.LabelIds).NotNullId();
    }
}

public sealed class UpdateCardDtoValidator : AbstractValidator<UpdateCardDto>
{
    public UpdateCardDtoValidator()
    {
        RuleFor(x => x.Title).Required().MaxLength(FieldLimits.CardTitle);
        RuleFor(x => x.Description).MaxLength(FieldLimits.CardDescription);
        RuleFor(x => x.IsDueComplete).Required().CompleteNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.DueReminderMinutes).NotNegative().ReminderNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.StartDate).NotAfterDueDate(x => x.DueDate);
        RuleForEach(x => x.LabelIds).NotNullId();
    }
}

public sealed class MoveCardDtoValidator : AbstractValidator<MoveCardDto>
{
    public MoveCardDtoValidator()
    {
        RuleFor(x => x.ListId).Required();
        this.ValidPlacement();
    }
}

public sealed class CopyCardDtoValidator : AbstractValidator<CopyCardDto>
{
    public CopyCardDtoValidator()
    {
        RuleFor(x => x.ListId).Required();
        RuleFor(x => x.Title).MaxLength(FieldLimits.CardTitle);
        this.ValidPlacement();
    }
}

public sealed class CoverDtoValidator : AbstractValidator<CoverDto>
{
    public CoverDtoValidator()
    {
        RuleFor(x => x)
            .Must(cover => cover.AttachmentId is null || cover.Color is null)
            .WithErrorCode(ErrorCodes.MutuallyExclusive)
            .WithMessage("'attachmentId' can't be sent with 'color'.");
    }
}

internal static class CardRules
{
    public static IRuleBuilderOptions<T, bool?> CompleteNeedsDueDate<T>(
        this IRuleBuilder<T, bool?> rule, Func<T, DateTime?> dueDate) =>
        rule.Must((card, isDueComplete) => isDueComplete != true || dueDate(card) is not null)
            .WithErrorCode(ErrorCodes.RequiresDueDate)
            .WithMessage("A card can only be complete when it has a due date.");

    public static IRuleBuilderOptions<T, int?> ReminderNeedsDueDate<T>(
        this IRuleBuilder<T, int?> rule, Func<T, DateTime?> dueDate) =>
        rule.Must((card, minutes) => minutes is null || dueDate(card) is not null)
            .WithErrorCode(ErrorCodes.RequiresDueDate)
            .WithMessage("A card can only have a reminder when it has a due date.");

    public static IRuleBuilderOptions<T, DateTime?> NotAfterDueDate<T>(
        this IRuleBuilder<T, DateTime?> rule, Func<T, DateTime?> dueDate) =>
        rule.Must((card, startDate) => startDate is null || dueDate(card) is null || startDate <= dueDate(card))
            .WithErrorCode(ErrorCodes.StartAfterDue)
            .WithMessage("'{PropertyName}' must be on or before 'dueDate'.");
}
