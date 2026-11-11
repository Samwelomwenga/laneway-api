using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class DataProtectionKeyTests : IAsyncLifetime
{
    private const string Purpose = "Laneway.Tests";

    private readonly TestStack _stack;
    private Guid _databaseId;

    public DataProtectionKeyTests(TestStack stack) => _stack = stack;

    public async Task InitializeAsync() => _databaseId = await _stack.NewDatabaseAsync();

    public Task DisposeAsync() => _stack.DropDatabaseAsync(_databaseId);

    [Fact]
    public async Task A_payload_protected_before_a_recreate_unprotects_after_it()
    {
        var connectionString = _stack.ConnectionStringFor(_databaseId);
        string token;
        await using (var before = new ApiFactory(connectionString))
        {
            token = before.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(Purpose)
                .Protect("someone@example.com");
        }

        await using var after = new ApiFactory(connectionString);
        var payload = after.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Purpose)
            .Unprotect(token);

        Assert.Equal("someone@example.com", payload);
        using var scope = after.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await database.DataProtectionKeys.AnyAsync());
    }
}
