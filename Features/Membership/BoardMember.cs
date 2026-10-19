namespace Laneway.Api;

public class BoardMember : BaseEntity
{
    public required Guid BoardId { get; set; }
    public required Guid UserId { get; set; }
    public required BoardRole Role { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Active;

    public static BoardMember AdminOf(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        return new BoardMember
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            UserId = board.CreatedBy,
            Role = BoardRole.Admin,
            CreatedAt = board.CreatedAt,
            CreatedBy = board.CreatedBy
        };
    }
}
