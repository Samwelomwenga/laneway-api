using Xunit;

namespace Laneway.Api.Tests;

public static class ActivityAssert
{
    public static void Place(
        ActivityEntryDto entry,
        Guid? workspaceId = null,
        Guid? boardId = null,
        Guid? listId = null,
        Guid? cardId = null,
        Guid? fromWorkspaceId = null,
        Guid? fromBoardId = null,
        Guid? fromListId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Assert.Equal(workspaceId, entry.WorkspaceId);
        Assert.Equal(boardId, entry.BoardId);
        Assert.Equal(listId, entry.ListId);
        Assert.Equal(cardId, entry.CardId);
        Assert.Equal(fromWorkspaceId, entry.FromWorkspaceId);
        Assert.Equal(fromBoardId, entry.FromBoardId);
        Assert.Equal(fromListId, entry.FromListId);
    }

    public static void Actor(ActivityEntryDto entry, UserDto actor)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(actor);

        Assert.Equal(actor.Id, entry.CreatedBy);
        Assert.Equal(actor.Id, entry.Data.Id("actor.id"));
        Assert.Equal(actor.Username, entry.Data.Text("actor.username"));
        Assert.Equal(actor.FirstName, entry.Data.Text("actor.firstName"));
        Assert.Equal(actor.LastName, entry.Data.Text("actor.lastName"));
    }
}
