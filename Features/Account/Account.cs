
namespace DefaultNamespace;

public class Account : BaseEntity
{
    public required string Name { get; set; }
    public Guid UserId { get; set; }
    public bool IsActive { get; set; }
    public User? User { get; set; }
}
