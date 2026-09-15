using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateCardDtoValidator : AbstractValidator<CreateCardDto>
{
    public CreateCardDtoValidator()
    {
        RuleFor(x => x.Title).Required();
    }
}

public sealed class UpdateCardDtoValidator : AbstractValidator<UpdateCardDto>
{
    public UpdateCardDtoValidator()
    {
        RuleFor(x => x.Title).Required();
    }
}
