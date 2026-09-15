using FluentValidation;

namespace DefaultNamespace;

public sealed class CreateBoardDtoValidator : AbstractValidator<CreateBoardDto>
{
    public CreateBoardDtoValidator()
    {
        RuleFor(x => x.Name).Required();
    }
}

public sealed class UpdateBoardDtoValidator : AbstractValidator<UpdateBoardDto>
{
    public UpdateBoardDtoValidator()
    {
        RuleFor(x => x.Name).Required();
    }
}
