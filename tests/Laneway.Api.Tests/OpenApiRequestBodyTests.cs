using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class OpenApiRequestBodyTests : ApiTests
{
    private static readonly SortedDictionary<string, string[]> RequiredFields = new(StringComparer.Ordinal)
    {
        ["AddBoardMemberDto"] = [],
        ["AddWorkspaceMemberDto"] = [],
        ["ArchivedDto"] = ["value"],
        ["ChangeEmailDto"] = ["newEmail", "password"],
        ["ChangePasswordDto"] = ["currentPassword", "newPassword"],
        ["CheckedDto"] = ["value"],
        ["CommentTextDto"] = ["text"],
        ["ConfirmEmailChangeDto"] = ["token"],
        ["CopyBoardDto"] = ["workspaceId"],
        ["CopyCardDto"] = ["listId"],
        ["CopyListDto"] = ["boardId"],
        ["CoverDto"] = [],
        ["CreateAccountDto"] = ["name"],
        ["CreateBoardDto"] = ["name"],
        ["CreateCardDto"] = ["isDueComplete", "title"],
        ["CreateCheckItemDto"] = ["name"],
        ["CreateChecklistDto"] = ["name"],
        ["CreateLabelDto"] = [],
        ["CreateLinkAttachmentDto"] = ["url"],
        ["CreateListDto"] = ["name"],
        ["CreateWorkSpaceDto"] = ["name", "visibility"],
        ["DeleteUserDto"] = ["password"],
        ["ForgotPasswordDto"] = ["email"],
        ["MoveBoardDto"] = ["workspaceId"],
        ["MoveCardDto"] = ["listId"],
        ["MoveCheckItemDto"] = ["checklistId"],
        ["MoveListDto"] = ["boardId"],
        ["ReorderChecklistDto"] = [],
        ["ResendVerificationDto"] = ["email"],
        ["ResetPasswordDto"] = ["newPassword", "token"],
        ["SignInDto"] = ["email", "password"],
        ["SignUpDto"] = ["email", "firstName", "language", "lastName", "location", "password", "timeZone", "username"],
        ["UndoEmailChangeDto"] = ["newPassword", "recentPassword", "token"],
        ["UnsubscribeInvitesDto"] = ["token"],
        ["UpdateAccountDto"] = ["name"],
        ["UpdateAttachmentDto"] = ["name"],
        ["UpdateBoardDto"] = ["name"],
        ["UpdateBoardMemberDto"] = ["role"],
        ["UpdateBoardSettingsDto"] = ["addingMembers", "commenting", "selfJoin", "visibility"],
        ["UpdateCardDto"] = ["isDueComplete", "title"],
        ["UpdateCheckItemDto"] = ["name"],
        ["UpdateChecklistDto"] = ["name"],
        ["UpdateLabelDto"] = [],
        ["UpdateListDto"] = ["name"],
        ["UpdateProfileDto"] = ["firstName", "language", "lastName", "location", "timeZone", "username"],
        ["UpdateWorkSpaceDto"] = ["name", "visibility"],
        ["UpdateWorkspaceMemberDto"] = ["role"],
        ["VerifyEmailDto"] = ["token"]
    };

    public OpenApiRequestBodyTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Each_request_body_requires_the_fields_its_validator_requires()
    {
        var bodies = await RequestBodySchemasAsync();

        var required = bodies.ToDictionary(
            body => body.Key,
            body => body.Value.TryGetProperty("required", out var names)
                ? string.Join(", ", names.EnumerateArray().Select(name => name.GetString()).Order(StringComparer.Ordinal))
                : "");

        Assert.Equal(
            RequiredFields.ToDictionary(body => body.Key, body => string.Join(", ", body.Value)),
            new SortedDictionary<string, string>(required, StringComparer.Ordinal));
    }

    [Fact]
    public async Task No_required_request_body_field_allows_null()
    {
        var bodies = await RequestBodySchemasAsync();

        var nullable = bodies
            .SelectMany(body => body.Value.TryGetProperty("required", out var names)
                ? names.EnumerateArray()
                    .Select(name => name.GetString()!)
                    .Where(name => AllowsNull(body.Value.GetProperty("properties").GetProperty(name)))
                    .Select(name => $"{body.Key}.{name}")
                : [])
            .ToList();

        Assert.Empty(nullable);
    }

    private async Task<SortedDictionary<string, JsonElement>> RequestBodySchemasAsync()
    {
        using var response = await SendAnonymousAsync(HttpMethod.Get, "/openapi/v1.json", null);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var schemas = document.GetProperty("components").GetProperty("schemas");

        return new SortedDictionary<string, JsonElement>(
            document.GetProperty("paths").EnumerateObject()
                .SelectMany(path => path.Value.EnumerateObject())
                .Where(operation => operation.Value.TryGetProperty("requestBody", out _))
                .SelectMany(operation => operation.Value.GetProperty("requestBody").GetProperty("content").EnumerateObject())
                .Where(content => content.Value.GetProperty("schema").TryGetProperty("$ref", out _))
                .Select(content => content.Value.GetProperty("schema").GetProperty("$ref").GetString()!.Split('/')[^1])
                .Distinct()
                .ToDictionary(name => name, name => schemas.GetProperty(name)),
            StringComparer.Ordinal);
    }

    private static bool AllowsNull(JsonElement property) =>
        property.TryGetProperty("type", out var type)
            ? type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(name => name.GetString() == "null")
            : property.TryGetProperty("oneOf", out var oneOf)
              && oneOf.EnumerateArray().Any(member => member.TryGetProperty("type", out var memberType) && memberType.GetString() == "null");
}
