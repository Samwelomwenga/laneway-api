using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class UsernameTests : ApiTests
{
    public UsernameTests(TestStack stack) : base(stack)
    {
    }

    public static TheoryData<string> BrokenNames =>
    [
        "ab",
        "-ana",
        "_ana",
        "ana wanjiru",
        "ana.w",
        "ana@example.com",
        "\u0430na",
        "an\u00e4",
        "\u212Aamau",
        "\u0130mani"
    ];

    [Fact]
    public async Task Sign_up_trims_and_lowercases_the_username()
    {
        var email = NewEmail();
        using var response = await SignUpAsync(SignUpBody(email, username: "  Ana_W-2 "));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("ana_w-2", await FromDatabaseAsync(database =>
            database.Users.Where(u => u.Email == email).Select(u => u.Username).SingleAsync()));
    }

    [Theory]
    [MemberData(nameof(BrokenNames))]
    public async Task Sign_up_refuses_a_username_that_breaks_the_rules(string username)
    {
        using var response = await SignUpAsync(SignUpBody(NewEmail(), username: username));

        await AssertInvalidUsernameAsync(response);
    }

    [Fact]
    public async Task A_username_that_differs_only_in_case_is_taken()
    {
        using var first = await SignUpAsync(SignUpBody(NewEmail(), username: "wanjiru"));

        using var response = await SignUpAsync(SignUpBody(NewEmail(), username: "WanJiru"));

        await AssertTakenUsernameAsync(response);
    }

    [Fact]
    public async Task A_username_taken_between_the_check_and_the_save_is_409()
    {
        var other = await CreateUserAsync();
        PwnedPasswords.BeforeAnswer = () => FromDatabaseAsync(database => database.Users
            .Where(user => user.Id == other.Id)
            .ExecuteUpdateAsync(user => user.SetProperty(u => u.Username, "wanjiru")));

        using var response = await SignUpAsync(SignUpBody(NewEmail(), username: "wanjiru"));

        await AssertTakenUsernameAsync(response);
    }

    [Fact]
    public async Task Profile_update_trims_and_lowercases_the_username()
    {
        using var response = await PutAsync("/api/v1/profile", ProfileBody(" WanJiru "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("wanjiru", (await response.ReadEnvelope<UserDto>()).Data!.Username);
    }

    [Theory]
    [MemberData(nameof(BrokenNames))]
    public async Task Profile_update_refuses_a_username_that_breaks_the_rules(string username)
    {
        using var response = await PutAsync("/api/v1/profile", ProfileBody(username));

        await AssertInvalidUsernameAsync(response);
    }

    [Fact]
    public async Task Member_lookup_trims_and_lowercases_the_username()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var user = await CreateUserAsync();

        using var response = await PostAsync(
            WorkspaceMembersPath(workspace.Id), new { username = $"  {user.User.Username.ToUpperInvariant()} " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Member_lookup_skips_the_character_rule()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await PostAsync(WorkspaceMembersPath(workspace.Id), new { username = "a.b" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("username", error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task The_checker_says_a_free_name_is_available()
    {
        Assert.True(await AvailableAsync("wanjiru"));
    }

    [Fact]
    public async Task The_checker_says_a_taken_name_in_any_case_is_not_available()
    {
        Assert.False(await AvailableAsync($" {ActorUser.User.Username.ToUpperInvariant()} "));
    }

    [Theory]
    [MemberData(nameof(BrokenNames))]
    public async Task The_checker_says_a_name_that_breaks_the_rules_is_not_available(string username)
    {
        Assert.False(await AvailableAsync(username));
    }

    [Fact]
    public async Task The_checker_says_a_missing_name_is_not_available()
    {
        using var response = await SendAnonymousAsync(HttpMethod.Get, "/api/v1/auth/username-available", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.ReadEnvelope<UsernameAvailabilityDto>()).Data!.Available);
    }

    [Fact]
    public async Task The_migration_lowercases_names_and_renames_the_newer_of_a_pair_and_every_broken_one()
    {
        var now = Time.GetUtcNow().UtcDateTime;
        var (oldest, newer, broken, kept) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await MigrateWithUsersAsync(
            "LowercaseUsernames",
            (oldest, "Bob", now.AddDays(-2)),
            (newer, "bob", now.AddDays(-1)),
            (broken, "Ana Wanjiru", now),
            (kept, "  Kamau ", now));

        var names = await FromDatabaseAsync(database => database.Users
            .AsNoTracking()
            .Where(user => new[] { oldest, newer, broken, kept }.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Username));
        Assert.Equal("bob", names[oldest]);
        Assert.Equal($"user-{newer:n}"[..13], names[newer]);
        Assert.Equal($"user-{broken:n}"[..13], names[broken]);
        Assert.Equal("kamau", names[kept]);
    }

    private async Task<bool> AvailableAsync(string username)
    {
        using var response = await SendAnonymousAsync(
            HttpMethod.Get, $"/api/v1/auth/username-available?username={Uri.EscapeDataString(username)}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<UsernameAvailabilityDto>()).Data!.Available;
    }

    private static async Task AssertTakenUsernameAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("username", error.Field);
        Assert.Equal(ErrorCodes.Duplicate, error.Code);
    }

    private static async Task AssertInvalidUsernameAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("username", error.Field);
        Assert.Equal(ErrorCodes.Invalid, error.Code);
    }

    private static Dictionary<string, object?> ProfileBody(string username) => Fields(new
    {
        username,
        firstName = "Wanjiru",
        lastName = "Kamau",
        language = "en-GB",
        timeZone = "Africa/Nairobi",
        location = "Nakuru"
    });
}
