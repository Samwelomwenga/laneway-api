using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class MessagesTests : ApiTests
{
    public MessagesTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_rolled_back_transaction_sends_nothing()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync();
            scope.ServiceProvider.GetRequiredService<Messages>().Add(PasswordNotice());
            await database.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
    }

    [Fact]
    public async Task A_committed_message_goes_out_once()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<Messages>().Add(PasswordNotice());
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        var send = await AwaitEmailAsync(ActorUser.User.Email);
        Assert.Equal("Your Laneway password was changed", send.Subject);
        Assert.Single(Brevo.Sends);
    }
}
