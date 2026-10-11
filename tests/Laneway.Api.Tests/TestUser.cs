namespace Laneway.Api.Tests;

public sealed record TestUser(UserDto User, Guid SessionId, string AccessToken)
{
    public Guid Id => User.Id;
}
