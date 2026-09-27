using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class ActivityRouteTests : ApiTests
{
    public ActivityRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task With_no_filter_every_entry_comes_back_newest_first()
    {
        var first = await CreateWorkspaceAsync("Design");
        var second = await CreateWorkspaceAsync("Research");

        var page = await ActivityAsync();

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, page.Entries[0].WorkspaceId);
        Assert.Equal(first.Id, page.Entries[1].WorkspaceId);
        Assert.True(page.Entries[0].CreatedAt > page.Entries[1].CreatedAt);
    }

    [Fact]
    public async Task Type_takes_a_comma_separated_list_of_names_in_any_case()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        using var renamed = await UpdateWorkspaceAsync(workspace.Id, "Research");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var both = await ActivityAsync("type=CREATEWORKSPACE,updateworkspace");
        var one = await ActivityAsync("type=updateWorkspace");

        Assert.Equal(2, both.TotalCount);
        Assert.Equal(ActivityType.UpdateWorkspace, Assert.Single(one.Entries).Type);
    }

    [Fact]
    public async Task An_unknown_type_name_is_unknownValue()
    {
        var errors = await ActivityErrorsAsync("type=createWorkspace,createEpic");

        var error = Assert.Single(errors);
        Assert.Equal("type", error.Field);
        Assert.Equal(ErrorCodes.UnknownValue, error.Code);
        Assert.Contains("createEpic", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_entry_in_type_is_invalidFormat()
    {
        var errors = await ActivityErrorsAsync("type=createWorkspace,,updateWorkspace");

        var error = Assert.Single(errors);
        Assert.Equal("type", error.Field);
        Assert.Equal(ErrorCodes.InvalidFormat, error.Code);
    }

    [Fact]
    public async Task The_archived_filter_doesnt_apply_here()
    {
        var errors = await ActivityErrorsAsync("archived=only");

        var error = Assert.Single(errors);
        Assert.Equal("archived", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Sending_type_twice_is_invalidFormat()
    {
        var errors = await ActivityErrorsAsync("type=createWorkspace&type=updateWorkspace");

        var error = Assert.Single(errors);
        Assert.Equal("type", error.Field);
        Assert.Equal(ErrorCodes.InvalidFormat, error.Code);
    }

    [Fact]
    public async Task Before_takes_entries_at_or_before_it_and_since_takes_the_ones_after()
    {
        var first = await CreateWorkspaceAsync("Design");
        var second = await CreateWorkspaceAsync("Research");
        var firstEntry = Assert.Single((await ActivityAsync($"workspaceId={first.Id}")).Entries);

        var upTo = await ActivityAsync($"before={Moment(firstEntry.CreatedAt)}");
        var after = await ActivityAsync($"since={Moment(firstEntry.CreatedAt)}");

        Assert.Equal(first.Id, Assert.Single(upTo.Entries).WorkspaceId);
        Assert.Equal(second.Id, Assert.Single(after.Entries).WorkspaceId);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("since")]
    public async Task A_timestamp_with_no_offset_is_invalidFormat(string key)
    {
        var errors = await ActivityErrorsAsync($"{key}=2026-09-27T10:00:00");

        var error = Assert.Single(errors);
        Assert.Equal(key, error.Field);
        Assert.Equal(ErrorCodes.InvalidFormat, error.Code);
    }

    [Fact]
    public async Task An_id_that_names_neither_a_row_nor_an_entry_is_notFound_on_its_own_key()
    {
        var missing = Guid.NewGuid();

        var errors = await ActivityErrorsAsync($"workspaceId={missing}&cardId={Guid.NewGuid()}");

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(ErrorCodes.NotFound, error.Code));
        var workspace = Assert.Single(errors, error => error.Field == "workspaceId");
        Assert.Contains(missing.ToString(), workspace.Message, StringComparison.Ordinal);
        Assert.Single(errors, error => error.Field == "cardId");
    }

    [Fact]
    public async Task A_deleted_workspace_still_reads_its_own_log()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        using var response = await DeleteAsync($"/api/v1/workspaces/{workspace.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"workspaceId={workspace.Id}");

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task An_id_filter_that_names_a_live_row_with_no_entries_returns_an_empty_page()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Sprint 1");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        var page = await ActivityAsync($"cardId={card.Id}");

        Assert.Empty(page.Entries);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Filters_combine_with_and_and_a_known_id_that_matches_nothing_is_no_error()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        var page = await ActivityAsync($"workspaceId={workspace.Id}&type=deleteWorkspace");

        Assert.Empty(page.Entries);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task The_actor_header_is_ignored()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await GetAsync(
            $"/api/v1/activity?workspaceId={workspace.Id}", actorHeader: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.ReadPage<ActivityEntryDto>()).TotalCount);
    }
}
