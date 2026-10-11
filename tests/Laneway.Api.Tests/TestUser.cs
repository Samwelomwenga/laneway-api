namespace Laneway.Api.Tests;

public sealed record TestUser(Guid Id, Guid SessionId, string AccessToken);
