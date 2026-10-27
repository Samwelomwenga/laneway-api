using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ProfileTests : ApiTests
{
    public ProfileTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Get_profile_returns_the_callers_full_user()
    {
        var other = await CreateUserAsync();

        using var response = await GetAsync("/api/v1/profile", other);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = (await response.ReadEnvelope<UserDto>()).Data!;
        Assert.Equal(other.Id, profile.Id);
        Assert.Equal(other.User.Email, profile.Email);
        Assert.Equal(other.Id, Assert.Single(profile.Accounts).UserId);
    }

    [Fact]
    public async Task Put_profile_edits_only_the_caller()
    {
        var other = await CreateUserAsync();

        using var response = await PutAsync("/api/v1/profile", ProfileBody(username: "wanjiru"), other);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.ReadEnvelope<UserDto>()).Data!;
        Assert.Equal(other.Id, edited.Id);
        Assert.Equal("wanjiru", edited.Username);
        Assert.Equal("Wanjiru", edited.FirstName);
        Assert.Equal(other.User.Email, edited.Email);
        Assert.Equal(other.Id, edited.UpdatedBy);
        using var mine = await GetAsync("/api/v1/profile");
        Assert.Equal(ActorUser.User.Username, (await mine.ReadEnvelope<UserDto>()).Data!.Username);
    }

    [Fact]
    public async Task Changing_the_username_logs_username_changed_without_either_name()
    {
        const string ip = "198.51.100.9";

        using var response = await SendFromAsync(ip, HttpMethod.Put, "/api/v1/profile", ActorUser, ProfileBody(username: "wanjiru"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = Assert.Single(LogsOf(AuthLog.Category, "UsernameChanged"));
        Assert.Equal(
            (ActorId.ToString(), ip),
            (log.GetStructuredStateValue("sub"), log.GetStructuredStateValue(ClientIp.Field)));
        Assert.DoesNotContain("wanjiru", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ActorUser.User.Username, log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_profile_put_that_keeps_the_username_logs_no_username_change()
    {
        using var response = await PutAsync("/api/v1/profile", ProfileBody(username: ActorUser.User.Username));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(LogsOf(AuthLog.Category, "UsernameChanged"));
    }

    [Fact]
    public async Task Put_profile_takes_no_email()
    {
        var body = new Dictionary<string, object?>(Fields(ProfileBody()))
        {
            ["email"] = "new@example.com"
        };

        using var response = await PutAsync("/api/v1/profile", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("email", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Put_profile_with_someone_elses_username_is_409()
    {
        var other = await CreateUserAsync();

        using var response = await PutAsync("/api/v1/profile", ProfileBody(username: other.User.Username));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("username", error.Field);
        Assert.Equal(ErrorCodes.Duplicate, error.Code);
    }

    [Fact]
    public async Task Put_profile_keeping_the_callers_own_username_is_fine()
    {
        using var response = await PutAsync("/api/v1/profile", ProfileBody(username: ActorUser.User.Username));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_user_shows_up_as_a_summary_with_no_contact_details()
    {
        var other = await CreateUserAsync();

        using var response = await GetAsync($"/api/v1/users/{other.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var fields = json.RootElement.GetProperty("data").EnumerateObject().Select(p => p.Name);
        Assert.Equal(
            ["bio", "firstName", "id", "lastName", "profilePictureUrl", "username"],
            fields.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(other.User.Email, body, StringComparison.OrdinalIgnoreCase);
        var summary = (await response.ReadEnvelope<UserSummaryDto>()).Data!;
        Assert.Equal(other.Id, summary.Id);
        Assert.Equal(other.User.Username, summary.Username);
    }

    [Fact]
    public async Task A_missing_user_is_404()
    {
        using var response = await GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_user_list_username_check_and_user_edit_are_gone()
    {
        using var list = await GetAsync("/api/v1/users");
        using var exists = await GetAsync($"/api/v1/users/username-exists?username={ActorUser.User.Username}");
        using var edit = await PutAsync($"/api/v1/users/{ActorId}", Fields(ProfileBody()));

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, exists.StatusCode);
        Assert.Equal("id", Assert.Single(await exists.ReadErrors()).Field);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, edit.StatusCode);
    }

    public static TheoryData<string, int> ProfileLimits => new()
    {
        { "username", FieldLimits.Username },
        { "firstName", FieldLimits.UserFirstName },
        { "lastName", FieldLimits.UserLastName },
        { "phoneNumber", FieldLimits.UserPhoneNumber },
        { "bio", FieldLimits.UserBio },
        { "language", FieldLimits.UserLanguage },
        { "timeZone", FieldLimits.UserTimeZone },
        { "location", FieldLimits.UserLocation }
    };

    [Theory]
    [MemberData(nameof(ProfileLimits))]
    public async Task A_profile_field_over_the_limit_is_tooLong(string field, int limit)
    {
        var body = Fields(ProfileBody());
        body[field] = new string('a', limit + 1);

        using var response = await PutAsync("/api/v1/profile", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(field, error.Field);
        Assert.Equal(ErrorCodes.TooLong, error.Code);
    }

    private static object ProfileBody(string username = "wanjiru") => new
    {
        username,
        firstName = "Wanjiru",
        lastName = "Kamau",
        phoneNumber = (string?)null,
        bio = "Plans the sprints.",
        language = "en-GB",
        timeZone = "Africa/Nairobi",
        location = "Nakuru",
        profilePictureUrl = (string?)null
    };
}
