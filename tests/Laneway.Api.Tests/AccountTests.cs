using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class AccountTests : ApiTests
{
    public AccountTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task The_account_list_holds_only_the_callers_accounts()
    {
        var other = await CreateUserAsync();
        await CreateAccountAsync("Side projects");

        using var response = await GetAsync($"/api/v1/accounts?userId={ActorId}&pageSize=100", other);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadPage<AccountDto>();
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(other.Id, Assert.Single(page.Entries).UserId);
    }

    [Fact]
    public async Task An_account_search_without_a_page_size_returns_twenty_items()
    {
        for (var i = 0; i < 21; i++)
        {
            await CreateAccountAsync($"Account {i}");
        }

        using var response = await GetAsync("/api/v1/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadPage<AccountDto>();
        Assert.Equal(20, page.Entries.Count);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task A_new_account_belongs_to_the_caller()
    {
        var other = await CreateUserAsync();

        var account = await CreateAccountAsync("Side projects", other);

        Assert.Equal(other.Id, account.UserId);
        Assert.Equal(other.Id, account.CreatedBy);
    }

    [Fact]
    public async Task A_new_account_takes_no_user_id()
    {
        var other = await CreateUserAsync();

        using var response = await PostAsync("/api/v1/accounts", new { name = "Side projects", userId = other.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("userId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task The_caller_reads_edits_and_deletes_their_own_account()
    {
        var account = await CreateAccountAsync("Side projects");

        using var read = await GetAsync($"/api/v1/accounts/{account.Id}");
        using var edit = await PutAsync($"/api/v1/accounts/{account.Id}", new { name = "Hobbies" });
        using var delete = await DeleteAsync($"/api/v1/accounts/{account.Id}");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal("Hobbies", (await edit.ReadEnvelope<AccountDto>()).Data!.Name);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Someone_elses_account_looks_missing()
    {
        var other = await CreateUserAsync();
        var account = await CreateAccountAsync("Side projects");

        using var read = await GetAsync($"/api/v1/accounts/{account.Id}", other);
        using var edit = await PutAsync($"/api/v1/accounts/{account.Id}", new { name = "Taken over" }, other);
        using var delete = await DeleteAsync($"/api/v1/accounts/{account.Id}", other);
        using var missing = await GetAsync($"/api/v1/accounts/{Guid.NewGuid()}", other);

        var missingMessage = (await missing.ReadEnvelope<object>()).Message;
        foreach (var response in new[] { read, edit, delete })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(missingMessage, (await response.ReadEnvelope<object>()).Message);
        }

        var stored = await FromDatabaseAsync(database => database.Accounts.SingleAsync(a => a.Id == account.Id));
        Assert.Equal("Side projects", stored.Name);
    }

    private async Task<AccountDto> CreateAccountAsync(string name, TestUser? @as = null)
    {
        using var response = await PostAsync("/api/v1/accounts", new { name }, @as);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<AccountDto>()).Data!;
    }
}
