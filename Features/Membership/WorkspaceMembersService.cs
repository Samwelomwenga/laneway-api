using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IWorkspaceMembersService
{
    Task<ApiResponse<List<WorkspaceMemberDto>>> GetAllAsync(Guid workspaceId, MemberSearchDto searchDto);
    Task<ApiResponse<AddedWorkspaceMemberDto>> AddAsync(Guid workspaceId, AddMemberWrite<WorkspaceRole> write);
    Task<ApiResponse<WorkspaceMemberDto>> UpdateAsync(Guid workspaceId, Guid memberId, UpdateMemberWrite<WorkspaceRole> write);
    Task<ApiResponse<bool>> RemoveAsync(Guid workspaceId, Guid memberId);
}

public class WorkspaceMembersService : IWorkspaceMembersService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Readable _readable;
    private readonly ActivityWriter _activity;
    private readonly Invites _invites;

    public WorkspaceMembersService(
        ApplicationDbContext context, Actor actor, Readable readable, ActivityWriter activity, Invites invites)
    {
        _context = context;
        _actor = actor;
        _readable = readable;
        _activity = activity;
        _invites = invites;
    }

    public async Task<ApiResponse<List<WorkspaceMemberDto>>> GetAllAsync(Guid workspaceId, MemberSearchDto searchDto)
    {
        ArgumentNullException.ThrowIfNull(searchDto);

        var access = await _readable.WorkspaceAsync(workspaceId, _actor.Id);
        if (_readable.Denies(access, WorkspaceAction.ListMembers, workspaceId, _actor.Id, out ApiResponse<List<WorkspaceMemberDto>>? denied))
        {
            return denied;
        }

        var query =
            from member in _context.WorkspaceMembers
            where member.WorkspaceId == workspaceId
            join user in _context.Users on member.UserId equals user.Id
            orderby member.CreatedAt, member.UserId
            select new WorkspaceMemberDto(
                new MemberUserDto(user.Id, user.Username, user.FirstName, user.LastName, user.ProfilePictureUrl),
                member.Role,
                member.Status);

        var (members, totalCount) = await PagedQuery.ReadAsync(
            query, pageNumber: searchDto.PageNumber, pageSize: searchDto.PageSize);

        return PagedResponse<WorkspaceMemberDto>.SuccessResponse(
            members,
            totalCount,
            pageSize: searchDto.PageSize,
            pageNumber: searchDto.PageNumber,
            message: "Members retrieved successfully");
    }

    public async Task<ApiResponse<AddedWorkspaceMemberDto>> AddAsync(Guid workspaceId, AddMemberWrite<WorkspaceRole> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var userId = _actor.Id;
        var access = await _readable.WorkspaceAsync(workspaceId, userId);
        if (_readable.Denies(access, WorkspaceAction.Administer, workspaceId, userId, out ApiResponse<AddedWorkspaceMemberDto>? denied))
        {
            return denied;
        }

        var user = await _context.FindUserAsync(write.User);
        if (user is null)
        {
            return write.User.InvitableEmail is { } email
                ? await _invites.InviteAsync(
                    new InviteTarget(InvitePlace.Workspace, workspaceId, access.Target.Name),
                    email,
                    write.Role.ToString(),
                    invite => AddedWorkspaceMemberDto.Invited(invite, write.Role))
                : Members.UnknownUser<AddedWorkspaceMemberDto>(write.User);
        }

        if (await _context.WorkspaceMembers.AnyAsync(member => member.WorkspaceId == workspaceId && member.UserId == user.Id))
        {
            return Members.AlreadyMember<AddedWorkspaceMemberDto>("workspace");
        }

        var member = new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = write.Role,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        _context.WorkspaceMembers.Add(member);
        var workspace = WorkspaceRef.Of(access.Target);
        await _activity.AddAsync(
            ActivityType.AddMember,
            ActivityPlace.OnWorkspace(workspaceId),
            actor => new AddMemberData<WorkspaceRole>(actor, workspace, null, MemberRef.Of(user), member.Role));
        await _context.SaveChangesAsync();

        return ApiResponse<AddedWorkspaceMemberDto>.SuccessResponse(
            new AddedWorkspaceMemberDto(Members.UserOf(user), member.Role, member.Status, null, null),
            "Member added successfully",
            StatusCodes.Status201Created);
    }

    public async Task<ApiResponse<WorkspaceMemberDto>> UpdateAsync(
        Guid workspaceId, Guid memberId, UpdateMemberWrite<WorkspaceRole> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var userId = _actor.Id;
        var access = await _readable.WorkspaceAsync(workspaceId, userId);
        if (_readable.Denies(access, WorkspaceAction.Administer, workspaceId, userId, out ApiResponse<WorkspaceMemberDto>? denied))
        {
            return denied;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.TryLockWorkspaceAsync(workspaceId);
        var found = await MemberAsync(workspaceId, memberId);
        if (found is null)
        {
            return Members.NotFound<WorkspaceMemberDto>();
        }

        var (member, user) = found.Value;
        var old = member.Role;
        if (old != write.Role)
        {
            var change = MemberChange.NewRole(old == WorkspaceRole.Admin, write.Role == WorkspaceRole.Admin);
            if ((await _context.OtherWorkspaceMembersAsync(workspaceId, memberId)).Blocks(change) is { } code)
            {
                return MemberLimits.Blocked<WorkspaceMemberDto>(code, "workspace");
            }

            member.Role = write.Role;
            var workspace = WorkspaceRef.Of(access.Target);
            await _activity.AddAsync(
                ActivityType.ChangeMemberRole,
                ActivityPlace.OnWorkspace(workspaceId),
                actor => new ChangeMemberRoleData<WorkspaceRole>(
                    actor, workspace, null, MemberRef.Of(user), new RoleChange<WorkspaceRole>(old, write.Role)));
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return ApiResponse<WorkspaceMemberDto>.SuccessResponse(
            new WorkspaceMemberDto(Members.UserOf(user), member.Role, member.Status), "Member updated successfully");
    }

    public async Task<ApiResponse<bool>> RemoveAsync(Guid workspaceId, Guid memberId)
    {
        var userId = _actor.Id;
        var access = await _readable.WorkspaceAsync(workspaceId, userId);
        var leaving = memberId == userId;
        if (leaving
                ? _readable.ReadDenies(access, AccessTarget.Workspace, workspaceId, userId, out ApiResponse<bool>? denied)
                : _readable.Denies(access, WorkspaceAction.Administer, workspaceId, userId, out denied))
        {
            return denied;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.TryLockWorkspaceAsync(workspaceId);
        var found = await MemberAsync(workspaceId, memberId);
        if (found is null)
        {
            return Members.NotFound<bool>();
        }

        var (member, user) = found.Value;
        var onBoards = await _context.BoardMembers
            .Where(boardMember => boardMember.UserId == memberId)
            .Join(_context.Boards.Where(board => board.WorkspaceId == workspaceId),
                boardMember => boardMember.BoardId, board => board.Id,
                (boardMember, board) => new { Member = boardMember, Board = board })
            .OrderBy(row => row.Board.Name)
            .ToListAsync();
        await _context.LockBoardsAsync([.. onBoards.Select(row => row.Board.Id)]);

        List<ApiError> blocked = [];
        if ((await _context.OtherWorkspaceMembersAsync(workspaceId, memberId))
            .Blocks(MemberChange.Removal(member.Role == WorkspaceRole.Admin)) is { } code)
        {
            blocked.AddRange(MemberLimits.Blocked<bool>(code, "workspace").Errors);
        }

        foreach (var row in onBoards)
        {
            if ((await _context.OtherBoardMembersAsync(row.Board.Id, memberId))
                .Blocks(MemberChange.Removal(row.Member.Role == BoardRole.Admin)) is { } boardCode)
            {
                blocked.Add(MemberLimits.OnBoard(boardCode, row.Board.Name));
            }
        }

        if (blocked.Count > 0)
        {
            return MemberLimits.Conflict<bool>(blocked);
        }

        var workspace = WorkspaceRef.Of(access.Target);
        var removed = MemberRef.Of(user);
        _context.WorkspaceMembers.Remove(member);
        await _activity.AddAsync(
            ActivityType.RemoveMember,
            ActivityPlace.OnWorkspace(workspaceId),
            actor => new RemoveMemberData<WorkspaceRole>(actor, workspace, null, removed, member.Role));
        foreach (var row in onBoards)
        {
            _context.BoardMembers.Remove(row.Member);
            await _activity.AddAsync(
                ActivityType.RemoveMember,
                new ActivityPlace(WorkspaceId: workspaceId, BoardId: row.Board.Id),
                actor => new RemoveMemberData<BoardRole>(actor, workspace, BoardRef.Of(row.Board), removed, row.Member.Role));
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Member removed successfully", StatusCodes.Status204NoContent);
    }

    private async Task<(WorkspaceMember Member, User User)?> MemberAsync(Guid workspaceId, Guid memberId)
    {
        var found = await (
                from member in _context.WorkspaceMembers
                where member.WorkspaceId == workspaceId && member.UserId == memberId
                join user in _context.Users on member.UserId equals user.Id
                select new { Member = member, User = user })
            .FirstOrDefaultAsync();
        return found is null ? null : (found.Member, found.User);
    }
}
