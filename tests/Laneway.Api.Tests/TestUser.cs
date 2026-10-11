namespace Laneway.Api.Tests;

public sealed record TestUser(UserDto User, Guid SessionId, string AccessToken)
{
    public const string Password = "seeded plum tractor";

    public Guid Id => User.Id;
}
