using FluentValidation;

namespace Laneway.Api;

public sealed class AddWorkspaceMemberDtoValidator : AbstractValidator<AddWorkspaceMemberDto>
{
    public AddWorkspaceMemberDtoValidator() => this.NamesOneUser();
}

public sealed class AddBoardMemberDtoValidator : AbstractValidator<AddBoardMemberDto>
{
    public AddBoardMemberDtoValidator() => this.NamesOneUser();
}

public sealed class UpdateWorkspaceMemberDtoValidator : AbstractValidator<UpdateWorkspaceMemberDto>
{
    public UpdateWorkspaceMemberDtoValidator()
    {
        RuleFor(x => x.Role).Required();
    }
}

public sealed class UpdateBoardMemberDtoValidator : AbstractValidator<UpdateBoardMemberDto>
{
    public UpdateBoardMemberDtoValidator()
    {
        RuleFor(x => x.Role).Required();
    }
}

internal static class MemberRules
{
    public static void NamesOneUser<T>(this AbstractValidator<T> validator) where T : INamesAUser
    {
        validator.RuleFor(request => request)
            .Must(request => Named(request) > 0)
            .OverridePropertyName("userId")
            .WithErrorCode(ErrorCodes.Required)
            .WithMessage("Send one of 'userId', 'username', or 'email'.");
        validator.RuleFor(request => request)
            .Must(request => Named(request) < 2)
            .WithErrorCode(ErrorCodes.MutuallyExclusive)
            .WithMessage("Send only one of 'userId', 'username', or 'email'.");
        validator.RuleFor(request => request.Username)
            .Required()
            .MaxLength(FieldLimits.Username)
            .When(request => request.Username is not null);
        validator.RuleFor(request => request.Email).Required().When(request => request.Email is not null);
    }

    private static int Named(INamesAUser request) =>
        (request.UserId is null ? 0 : 1) + (request.Username is null ? 0 : 1) + (request.Email is null ? 0 : 1);
}
