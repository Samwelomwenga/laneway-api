using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed record InviteTarget(InvitePlace Place, Guid Id, string Name)
{
    public Guid? WorkspaceId => Place == InvitePlace.Workspace ? Id : null;

    public Guid? BoardId => Place == InvitePlace.Board ? Id : null;
}

public sealed class Invites(ApplicationDbContext database, Messages messages, Actor actor, TimeProvider time)
{
    public const int DailyEmails = 50;

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static readonly TimeSpan ResendAfter = TimeSpan.FromHours(24);

    public static readonly TimeSpan EmailWindow = TimeSpan.FromHours(24);

    private static readonly string InviterIdProperty = JsonNamingPolicy.CamelCase.ConvertName(nameof(InviteSent.InviterId));

    private const string LimitReached = "You've sent 50 invite emails in the last 24 hours. Try again later.";

    /// <summary>
    /// Invites an address with no verified user, or renews its live invite. A live invite keeps its role, and its
    /// email goes out again only once <see cref="ResendAfter"/> has passed.
    /// </summary>
    public async Task<ApiResponse<T>> InviteAsync<T>(InviteTarget target, string email, string role, Func<Invite, T> reply)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(reply);

        var now = time.GetUtcNow().UtcDateTime;
        var invite = await database.Invites.FirstOrDefaultAsync(i =>
            i.WorkspaceId == target.WorkspaceId && i.BoardId == target.BoardId && i.Email == email);
        var live = invite is not null && invite.ExpiresAt > now;
        if (live && invite!.Role != role)
        {
            return ApiResponse<T>.ErrorResponse("This address already has an invite", StatusCodes.Status409Conflict,
                [new ApiError(null, ErrorCodes.Duplicate,
                    "This address already has an invite with another role. Revoke it and invite again.")]);
        }

        var sendsEmail = !live || invite!.EmailedAt <= now - ResendAfter;
        if (sendsEmail && await EmailsReachedAsync(now))
        {
            return ApiResponse<T>.ErrorResponse(LimitReached, StatusCodes.Status429TooManyRequests,
                [new ApiError(null, ErrorCodes.TooManyRequests, LimitReached)]);
        }

        if (invite is null)
        {
            invite = new Invite
            {
                Id = Guid.NewGuid(),
                WorkspaceId = target.WorkspaceId,
                BoardId = target.BoardId,
                Email = email,
                Role = role
            };
            database.Invites.Add(invite);
        }

        if (!live)
        {
            invite.Role = role;
            invite.InvitedBy = actor.Id;
            invite.CreatedAt = now;
        }

        invite.ExpiresAt = now + Lifetime;
        if (sendsEmail)
        {
            var inviter = await database.Users.SingleAsync(user => user.Id == actor.Id);
            invite.EmailedAt = now;
            messages.Add(new InviteSent(
                email, inviter.Id, $"{inviter.FirstName} {inviter.LastName}", inviter.Username, target.Place, target.Name));
        }

        await database.SaveChangesAsync();
        return ApiResponse<T>.SuccessResponse(reply(invite), "Member invited", StatusCodes.Status201Created);
    }

    private async Task<bool> EmailsReachedAsync(DateTime now)
    {
        var since = now - EmailWindow;
        var inviter = actor.Id.ToString();
        return await database.Deliveries.CountAsync(d =>
            d.Type == MessageType.InviteSent
            && d.CreatedAt > since
            && d.Message.RootElement.GetProperty(InviterIdProperty).GetString() == inviter)
            >= DailyEmails;
    }
}
