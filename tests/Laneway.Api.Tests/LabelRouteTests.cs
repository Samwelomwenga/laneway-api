using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class LabelRouteTests : ApiTests
{
    public LabelRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_board_lists_only_its_own_labels()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var other = await CreateBoardAsync(workspace.Id, "Sprint 15");
        var label = await CreateLabelAsync(board.Id, "Bug");
        await CreateLabelAsync(other.Id, "Bug");

        var labels = await ReadLabelsOfBoardAsync(board.Id);

        Assert.Equal(label.Id, Assert.Single(labels).Id);
    }

    [Fact]
    public async Task The_collection_filters_by_searchTerm_and_orders_by_name_then_color()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var urgent = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        var bug = await CreateLabelAsync(board.Id, "Bug");
        var green = await CreateLabelAsync(board.Id, null, Color.Green);
        var blue = await CreateLabelAsync(board.Id, null, Color.Blue);

        Assert.Equal(urgent.Id, Assert.Single(await ReadLabelsOfBoardAsync(board.Id, "searchTerm=urg")).Id);
        Assert.Equal([blue.Id, green.Id, bug.Id, urgent.Id], (await ReadLabelsOfBoardAsync(board.Id)).Select(l => l.Id));
    }

    [Fact]
    public async Task A_create_puts_the_label_on_the_path_board()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await PostAsync(LabelsPath(board.Id), new { name = "Bug", color = "Red" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var label = (await response.ReadEnvelope<LabelDto>()).Data!;
        Assert.Equal(board.Id, label.BoardId);
        Assert.Equal(Color.Red, label.Color);
        Assert.Equal($"/api/v1/labels/{label.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal(label.Id, Assert.Single(await ReadLabelsOfBoardAsync(board.Id)).Id);
    }

    [Fact]
    public async Task A_duplicate_name_is_checked_against_the_path_board()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var other = await CreateBoardAsync(workspace.Id, "Sprint 15");
        await CreateLabelAsync(board.Id, "Bug");

        using var duplicate = await PostAsync(LabelsPath(board.Id), new { name = "bug" });
        using var elsewhere = await PostAsync(LabelsPath(other.Id), new { name = "Bug" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(ErrorCodes.Duplicate, Assert.Single(await duplicate.ReadErrors()).Code);
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task An_unknown_board_is_404_on_the_read_and_the_create()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync(LabelsPath(missing));
        using var create = await PostAsync(LabelsPath(missing), new { name = "Bug" });

        foreach (var response in new[] { read, create })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Board not found", (await response.ReadEnvelope<object>()).Message);
        }
    }

    [Fact]
    public async Task BoardId_in_the_query_is_unknownField()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await GetAsync($"{LabelsPath(board.Id)}?boardId={board.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task BoardId_in_the_create_body_is_unknownField()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await PostAsync(LabelsPath(board.Id), new { name = "Bug", boardId = board.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
        Assert.Empty(await ReadLabelsOfBoardAsync(board.Id));
    }

    [Fact]
    public async Task Body_and_query_errors_come_back_before_the_board_is_looked_up()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync($"{LabelsPath(missing)}?pageSize=0&color=red");
        using var create = await PostAsync(LabelsPath(missing), new { name = new string('x', 500) });

        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.Equal(["color", "pageSize"], (await read.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal("name", Assert.Single(await create.ReadErrors()).Field);
    }

    [Fact]
    public async Task An_archived_board_still_lists_its_labels()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var label = await CreateLabelAsync(board.Id, "Bug");
        await SetBoardArchivedAsync(board.Id, true);

        Assert.Equal(label.Id, Assert.Single(await ReadLabelsOfBoardAsync(board.Id)).Id);
    }

    [Fact]
    public async Task A_create_on_an_archived_board_is_409_archived_with_no_field()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");
        await SetBoardArchivedAsync(board.Id, true);

        using var response = await PostAsync(LabelsPath(board.Id), new { name = "Bug" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
        Assert.Empty(await ReadLabelsOfBoardAsync(board.Id));
    }

    [Fact]
    public async Task The_flat_label_collection_routes_are_gone()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var read = await GetAsync($"/api/v1/labels?boardId={board.Id}");
        using var create = await PostAsync("/api/v1/labels", new { name = "Bug", boardId = board.Id });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Empty(await ReadLabelsOfBoardAsync(board.Id));
    }
}
