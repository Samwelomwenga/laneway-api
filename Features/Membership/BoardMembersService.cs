using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IBoardMembersService
{
    Task<ApiResponse<List<BoardMemberDto>>> GetAllAsync(Guid boardId, MemberSearchDto searchDto);
    Task<ApiResponse<AddedBoardMemberDto>> AddAsync(Guid boardId, AddMemberWrite<BoardRole> write);
    Task<ApiResponse<BoardMemberDto>> UpdateAsync(Guid boardId, Guid memberId, UpdateMemberWrite<BoardRole> write);
    Task<ApiResponse<bool>> RemoveAsync(Guid boardId, Guid memberId);
    Task<ApiResponse<List<BoardInviteDto>>> GetInvitesAsync(Guid boardId, MemberSearchDto searchDto);
    Task<ApiResponse<bool>> RevokeInviteAsync(Guid boardId, Guid inviteId);
}

public class BoardMembersService : IBoardMembersService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Readable _readable;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;
    private readonly Invites _invites;
    private readonly ILogger _log;

    public BoardMembersService(
        ApplicationDbContext context,
        Actor actor,
        Readable readable,
        ActivityWriter activity,
        ActivityTree tree,
        Invites invites,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        _context = context;
        _actor = actor;
        _readable = readable;
        _activity = activity;
        _tree = tree;
        _invites = invites;
        _log = loggers.CreateLogger(AccessLog.Category);
    }

    public async Task<ApiResponse<List<BoardMemberDto>>> GetAllAsync(Guid boardId, MemberSearchDto searchDto)
    {
        ArgumentNullException.ThrowIfNull(searchDto);

        var access = await _readable.BoardAsync(boardId, _actor.Id);
        if (_readable.ReadDenies(access, AccessTarget.Board, boardId, _actor.Id, out ApiResponse<List<BoardMemberDto>>? denied))
        {
            return denied;
        }

        var workspaceId = access.Target.WorkspaceId;
        var query =
            from member in _context.BoardMembers
            where member.BoardId == boardId
            join user in _context.Users on member.UserId equals user.Id
            orderby member.CreatedAt, member.UserId
            select new BoardMemberDto(
                new MemberUserDto(user.Id, user.Username, user.FirstName, user.LastName, user.ProfilePictureUrl),
                member.Role,
                member.Status,
                !_context.WorkspaceMembers.Any(inWorkspace =>
                    inWorkspace.WorkspaceId == workspaceId && inWorkspace.UserId == member.UserId));

        var (members, totalCount) = await PagedQuery.ReadAsync(
            query, pageNumber: searchDto.PageNumber, pageSize: searchDto.PageSize);

        return PagedResponse<BoardMemberDto>.SuccessResponse(
            members,
            totalCount,
            pageSize: searchDto.PageSize,
            pageNumber: searchDto.PageNumber,
            message: "Members retrieved successfully");
    }

    public async Task<ApiResponse<AddedBoardMemberDto>> AddAsync(Guid boardId, AddMemberWrite<BoardRole> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var userId = _actor.Id;
        var access = await _readable.BoardAsync(boardId, userId);
        if (_readable.ReadDenies(access, AccessTarget.Board, boardId, userId, out ApiResponse<AddedBoardMemberDto>? denied))
        {
            return denied;
        }

        if (AddRefusal(access.Standing, write) is { } refusal)
        {
            return _readable.Refused<AddedBoardMemberDto>(refusal, AccessTarget.Board, boardId, userId);
        }

        var user = await _context.FindUserAsync(write.User);
        if (user is null)
        {
            return write.User.InvitableEmail is { } email
                ? await _invites.InviteAsync(
                    new InviteTarget(InvitePlace.Board, boardId, access.Target.Name),
                    email,
                    write.Role.ToString(),
                    invite => AddedBoardMemberDto.Invited(invite, write.Role))
                : Members.UnknownUser<AddedBoardMemberDto>(write.User);
        }

        if (await _context.BoardMembers.AnyAsync(member => member.BoardId == boardId && member.UserId == user.Id))
        {
            return Members.AlreadyMember<AddedBoardMemberDto>("board");
        }

        var board = access.Target;
        var member = new BoardMember
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            UserId = user.Id,
            Role = write.Role,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        _context.BoardMembers.Add(member);
        var chain = await _tree.BoardAsync(boardId);
        await _activity.AddAsync(
            ActivityType.AddMember,
            chain.Place,
            actor => new AddMemberData<BoardRole>(actor, chain.Workspace, chain.Board, MemberRef.Of(user), member.Role));
        await _context.SaveChangesAsync();
        _log.MemberAdded(userId, AccessTarget.Board, boardId, user.Id, member.Role);

        var isGuest = !await _context.WorkspaceMembers.AnyAsync(inWorkspace =>
            inWorkspace.WorkspaceId == board.WorkspaceId && inWorkspace.UserId == user.Id);
        return ApiResponse<AddedBoardMemberDto>.SuccessResponse(
            new AddedBoardMemberDto(Members.UserOf(user), member.Role, member.Status, isGuest, null, null),
            "Member added successfully",
            StatusCodes.Status201Created);
    }

    public async Task<ApiResponse<BoardMemberDto>> UpdateAsync(
        Guid boardId, Guid memberId, UpdateMemberWrite<BoardRole> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var userId = _actor.Id;
        var access = await _readable.BoardAsync(boardId, userId);
        if (_readable.Denies(access, BoardAction.Administer, AccessTarget.Board, boardId, userId, out ApiResponse<BoardMemberDto>? denied))
        {
            return denied;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.TryLockBoardAsync(boardId);
        var found = await MemberAsync(boardId, memberId);
        if (found is null)
        {
            return Members.NotFound<BoardMemberDto>();
        }

        var (member, user) = (found.Member, found.User);
        var old = member.Role;
        if (old != write.Role)
        {
            var change = MemberChange.NewRole(old == BoardRole.Admin, write.Role == BoardRole.Admin);
            if ((await _context.OtherBoardMembersAsync(boardId, memberId)).Blocks(change) is { } code)
            {
                return MemberLimits.Blocked<BoardMemberDto>(code, "board");
            }

            member.Role = write.Role;
            var chain = await _tree.BoardAsync(boardId);
            await _activity.AddAsync(
                ActivityType.ChangeMemberRole,
                chain.Place,
                actor => new ChangeMemberRoleData<BoardRole>(
                    actor, chain.Workspace, chain.Board, MemberRef.Of(user), new RoleChange<BoardRole>(old, write.Role)));
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        if (old != write.Role)
        {
            _log.MemberRoleChanged(userId, AccessTarget.Board, boardId, memberId, write.Role, old);
        }

        return ApiResponse<BoardMemberDto>.SuccessResponse(
            new BoardMemberDto(Members.UserOf(user), member.Role, member.Status, found.IsGuest),
            "Member updated successfully");
    }

    public async Task<ApiResponse<bool>> RemoveAsync(Guid boardId, Guid memberId)
    {
        var userId = _actor.Id;
        var access = await _readable.BoardAsync(boardId, userId);
        var leaving = memberId == userId;
        if (leaving
                ? _readable.ReadDenies(access, AccessTarget.Board, boardId, userId, out ApiResponse<bool>? denied)
                : _readable.Denies(access, BoardAction.Administer, AccessTarget.Board, boardId, userId, out denied))
        {
            return denied;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.TryLockBoardAsync(boardId);
        var found = await MemberAsync(boardId, memberId);
        if (found is null)
        {
            return Members.NotFound<bool>();
        }

        var member = found.Member;
        var change = MemberChange.Removal(member.Role == BoardRole.Admin);
        if ((await _context.OtherBoardMembersAsync(boardId, memberId)).Blocks(change) is { } code)
        {
            return MemberLimits.Blocked<bool>(code, "board");
        }

        _context.BoardMembers.Remove(member);
        var chain = await _tree.BoardAsync(boardId);
        await _activity.AddAsync(
            ActivityType.RemoveMember,
            chain.Place,
            actor => new RemoveMemberData<BoardRole>(actor, chain.Workspace, chain.Board, MemberRef.Of(found.User), member.Role));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        _log.MemberRemoved(userId, AccessTarget.Board, boardId, memberId, member.Role);
        return ApiResponse<bool>.SuccessResponse(true, "Member removed successfully", StatusCodes.Status204NoContent);
    }

    public async Task<ApiResponse<List<BoardInviteDto>>> GetInvitesAsync(Guid boardId, MemberSearchDto searchDto)
    {
        if (await InviteDenialAsync<List<BoardInviteDto>>(boardId) is { } denied)
        {
            return denied;
        }

        return await _invites.ListAsync(InvitePlace.Board, boardId, searchDto, row => new BoardInviteDto(
            row.Invite.Id,
            row.Invite.Email,
            Enum.Parse<BoardRole>(row.Invite.Role),
            row.Inviter,
            row.Invite.CreatedAt,
            row.Invite.ExpiresAt));
    }

    public async Task<ApiResponse<bool>> RevokeInviteAsync(Guid boardId, Guid inviteId)
    {
        if (await InviteDenialAsync<bool>(boardId) is { } denied)
        {
            return denied;
        }

        return await _invites.RevokeAsync(InvitePlace.Board, boardId, inviteId);
    }

    private async Task<ApiResponse<T>?> InviteDenialAsync<T>(Guid boardId)
    {
        var userId = _actor.Id;
        var access = await _readable.BoardAsync(boardId, userId);
        if (_readable.ReadDenies(access, AccessTarget.Board, boardId, userId, out ApiResponse<T>? denied))
        {
            return denied;
        }

        return BoardRules.Refusal(access.Standing, BoardAction.AddMember) is { } refusal
            ? _readable.Refused<T>(refusal, AccessTarget.Board, boardId, userId)
            : null;
    }

    private Task<FoundMember?> MemberAsync(Guid boardId, Guid memberId) =>
        (from member in _context.BoardMembers
         where member.BoardId == boardId && member.UserId == memberId
         join user in _context.Users on member.UserId equals user.Id
         join board in _context.Boards on member.BoardId equals board.Id
         select new FoundMember
         {
             Member = member,
             User = user,
             IsGuest = !_context.WorkspaceMembers.Any(inWorkspace =>
                 inWorkspace.WorkspaceId == board.WorkspaceId && inWorkspace.UserId == member.UserId)
         })
        .FirstOrDefaultAsync();

    private sealed class FoundMember
    {
        public required BoardMember Member { get; init; }
        public required User User { get; init; }
        public required bool IsGuest { get; init; }
    }

    private Refusal? AddRefusal(BoardStanding standing, AddMemberWrite<BoardRole> write)
    {
        var refusal = BoardRules.Refusal(
            standing, write.Role == BoardRole.Admin ? BoardAction.GrantAdmin : BoardAction.AddMember);
        var joining = write.User.UserId == _actor.Id && !standing.IsMember && write.Role == BoardRole.Normal;
        return refusal is not null && joining ? BoardRules.Refusal(standing, BoardAction.SelfJoin) : refusal;
    }
}
