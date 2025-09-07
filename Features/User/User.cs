namespace DefaultNamespace;

public class User
{
    public Guid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; }
    public string? PhoneNumber { get; set; }
    public Uri? ProfilePictureUrl { get; set; }
    public string? Bio { get; set; }
    public required string Language { get; set; }
    public required string TimeZone { get; set; }
    public required string Location { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public ICollection<Account>? Accounts { get; set; }
}

