
namespace Laneway.Api;

public class Account : BaseEntity
{
    public required string Name { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
}
