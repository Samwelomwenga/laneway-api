using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

public record MemberUserDto
(
    Guid Id,
    string Username,
    string FirstName,
    string LastName,
    Uri? ProfilePictureUrl
);

public record WorkspaceMemberDto
(
    MemberUserDto User,
    WorkspaceRole Role,
    MemberStatus Status
);

public record BoardMemberDto
(
    MemberUserDto User,
    BoardRole Role,
    MemberStatus Status,
    bool IsGuest
);

public record AddedWorkspaceMemberDto
(
    MemberUserDto? User,
    WorkspaceRole Role,
    MemberStatus Status,
    string? Email,
    Guid? InviteId
)
{
    public static AddedWorkspaceMemberDto Invited(Invite invite, WorkspaceRole role)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return new AddedWorkspaceMemberDto(null, role, MemberStatus.Invited, invite.Email, invite.Id);
    }
}

public record AddedBoardMemberDto
(
    MemberUserDto? User,
    BoardRole Role,
    MemberStatus Status,
    bool? IsGuest,
    string? Email,
    Guid? InviteId
)
{
    public static AddedBoardMemberDto Invited(Invite invite, BoardRole role)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return new AddedBoardMemberDto(null, role, MemberStatus.Invited, null, invite.Email, invite.Id);
    }
}

[ModelBinder(typeof(SearchQueryBinder<MemberSearchDto>))]
public record MemberSearchDto
(
    int PageNumber,
    int PageSize
) : ISearchQuery<MemberSearchDto>
{
    public static MemberSearchDto Read(QueryReader query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new MemberSearchDto(query.PageNumber(), query.PageSize());
    }
}

public interface INamesAUser
{
    Guid? UserId { get; }
    string? Username { get; }
    string? Email { get; }
}

public record AddWorkspaceMemberDto
(
    Guid? UserId,
    string? Username,
    string? Email,
    WorkspaceRole? Role
) : INamesAUser
{
    public string? Username { get; init; } = Usernames.Normalize(Username);
    public string? Email { get; init; } = Emails.Normalize(Email);
}

public record AddBoardMemberDto
(
    Guid? UserId,
    string? Username,
    string? Email,
    BoardRole? Role
) : INamesAUser
{
    public string? Username { get; init; } = Usernames.Normalize(Username);
    public string? Email { get; init; } = Emails.Normalize(Email);
}

public record UpdateWorkspaceMemberDto
(
    WorkspaceRole? Role
);

public record UpdateBoardMemberDto
(
    BoardRole? Role
);
