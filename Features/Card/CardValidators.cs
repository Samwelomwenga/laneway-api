using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateCardDtoValidator : AbstractValidator<CreateCardDto>
{
    public CreateCardDtoValidator()
    {
        RuleFor(x => x.Title).Required().MaxLength(FieldLimits.CardTitle);
        RuleFor(x => x.Description).MaxLength(FieldLimits.CardDescription);
        RuleFor(x => x.Position).ValidPosition();
        RuleFor(x => x).OnePlacement(x => new Placement(x.Position, x.Before, x.After));
        RuleFor(x => x.ListId).Required();
        RuleFor(x => x.IsDueComplete).Required().CompleteNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.DueReminderMinutes).NotNegative().ReminderNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.StartDate).NotAfterDueDate(x => x.DueDate);
        RuleFor(x => x.IsArchived).Required();
        RuleForEach(x => x.LabelIds).NotNullId();
    }
}

public sealed class UpdateCardDtoValidator : AbstractValidator<UpdateCardDto>
{
    public UpdateCardDtoValidator()
    {
        RuleFor(x => x.Title).Required().MaxLength(FieldLimits.CardTitle);
        RuleFor(x => x.Description).MaxLength(FieldLimits.CardDescription);
        RuleFor(x => x.Position).Required();
        RuleFor(x => x.ListId).Required();
        RuleFor(x => x.IsDueComplete).Required().CompleteNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.DueReminderMinutes).NotNegative().ReminderNeedsDueDate(x => x.DueDate);
        RuleFor(x => x.StartDate).NotAfterDueDate(x => x.DueDate);
        RuleFor(x => x.IsArchived).Required();
        RuleForEach(x => x.LabelIds).NotNullId();
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
