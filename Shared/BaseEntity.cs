namespace DefaultNamespace;

public class BaseEntity
{
    public Guid Id { get; set; }
    public  DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
