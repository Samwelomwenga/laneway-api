using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<WorkSpace> WorkSpaces { get; set; }
    public DbSet<Board> Boards { get; set; }
    public DbSet<List> Lists { get; set; }
    public DbSet<Card> Cards { get; set; }
    public DbSet<Label> Labels { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Account> Accounts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id)
                .IsRequired();
            entity.Property(a => a.Name)
                .HasColumnType("varchar(100)")
                .IsRequired();
            entity.Property(a => a.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")
                .HasColumnType("timestamp with time zone");
            entity.Property(a => a.UpdatedAt)
            .HasColumnType("timestamp with time zone")
                .IsRequired(false);
            entity.HasOne(a => a.User)
                .WithMany(u => u.Accounts)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id)
                .IsRequired();
                entity.Property(u => u.FirstName)
                    .HasColumnType("varchar(50)")
                     .IsRequired();
            entity.Property(u => u.LastName)
                .HasColumnType("varchar(50)")
                .IsRequired();
            entity.Property(u => u.Username)
                .HasColumnType("varchar(50)")
                .IsRequired();
            entity.Property(u => u.Email)
                .HasColumnType("varchar(255)")
                .IsRequired();
            entity.HasIndex(u => u.Email)
                .IsUnique();
            entity.Property(u => u.PasswordHash)
                .HasColumnType("varchar(500)")
                .IsRequired();
            entity.Property(u => u.IsActive)
                .HasDefaultValue(true);
                        entity.Property(u => u.PhoneNumber)
                                    .HasColumnType("varchar(20)")
                                .HasDefaultValue(string.Empty);
            entity.Property(u => u.ProfilePictureUrl);
            entity.Property(u => u.Bio)
                .HasColumnType("varchar(500)")
                .HasDefaultValue(string.Empty);
          entity.Property(u => u.Language)
              .HasColumnType("varchar(10)")
             .HasDefaultValue("en-US");
            entity.Property(u => u.TimeZone)
                .HasColumnType("varchar(50)")
                .HasDefaultValue("UTC");
            entity.Property(u => u.Location)
                .HasColumnType("varchar(300)")
                .HasDefaultValue(string.Empty);
            entity.Property(u => u.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")
                .HasColumnType("timestamp with time zone");
            entity.Property(u => u.CreatedBy)
                .IsRequired();
            entity.Property(u => u.UpdatedAt)
                 .HasColumnType("timestamp with time zone")
                .IsRequired(false);
            entity.Property(u => u.UpdatedBy)
                .IsRequired(false);
        });

        modelBuilder.Entity<Card>()
            .HasMany(c => c.Labels)
            .WithMany(l => l.Cards)
            .UsingEntity(j => j.ToTable("CardLabels"));
    }
}
