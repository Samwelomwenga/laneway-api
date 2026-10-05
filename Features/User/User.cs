namespace Laneway.Api;

public class User : BaseEntity
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string? PhoneNumber { get; set; }
    public Uri? ProfilePictureUrl { get; set; }
    public string? Bio { get; set; }
    public required string Language { get; set; }
    public required string TimeZone { get; set; }
    public required string Location { get; set; }
    public DateTime? DeletedAt { get; set; }
    public ICollection<Account>? Accounts { get; set; }
}
