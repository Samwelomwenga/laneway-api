namespace Laneway.Api;

public sealed record UserLookup(Guid? UserId, string? Username, string? Email)
{
    public static UserLookup Of(INamesAUser dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new UserLookup(dto.UserId, dto.Username, dto.Email);
    }

    public string Field => UserId is not null ? "userId" : Username is not null ? "username" : "email";

    public string Value => UserId?.ToString() ?? Username ?? Email ?? string.Empty;

    public string? InvitableEmail => UserId is null && Username is null ? Email : null;
}

public sealed record AddMemberWrite<TRole>(UserLookup User, TRole Role) where TRole : struct, Enum;

public sealed record UpdateMemberWrite<TRole>(TRole Role) where TRole : struct, Enum;

public static class MemberWrites
{
    public static AddMemberWrite<WorkspaceRole> Of(AddWorkspaceMemberDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new AddMemberWrite<WorkspaceRole>(UserLookup.Of(dto), dto.Role ?? WorkspaceRole.Normal);
    }

    public static AddMemberWrite<BoardRole> Of(AddBoardMemberDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new AddMemberWrite<BoardRole>(UserLookup.Of(dto), dto.Role ?? BoardRole.Normal);
    }

    public static UpdateMemberWrite<WorkspaceRole> Of(UpdateWorkspaceMemberDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new UpdateMemberWrite<WorkspaceRole>(Writes.Required(dto.Role, "role"));
    }

    public static UpdateMemberWrite<BoardRole> Of(UpdateBoardMemberDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new UpdateMemberWrite<BoardRole>(Writes.Required(dto.Role, "role"));
    }
}
