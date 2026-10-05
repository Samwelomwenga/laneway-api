using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
    public DbSet<Checklist> Checklists { get; set; }
    public DbSet<CheckItem> CheckItems { get; set; }
    public DbSet<Attachment> Attachments { get; set; }
    public DbSet<PendingObjectDelete> PendingObjectDeletes { get; set; }
    public DbSet<ActivityEntry> ActivityEntries { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Account> Accounts { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }

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
            entity.HasQueryFilter(a => a.User != null && a.User.DeletedAt == null);
            ActorKeys(entity);
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
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.Property(u => u.PasswordHash)
                .HasColumnType("varchar(500)")
                .IsRequired();
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
            entity.Property(u => u.DeletedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired(false);
            entity.HasQueryFilter(u => u.DeletedAt == null);
            ActorKeys(entity);
        });

        modelBuilder.Entity<WorkSpace>(entity =>
        {
            entity.Property(ws => ws.Name).HasColumnType($"varchar({FieldLimits.WorkspaceName})");
            entity.Property(ws => ws.Description).HasColumnType($"varchar({FieldLimits.WorkspaceDescription})");
            ActorKeys(entity);
        });

        modelBuilder.Entity<Board>(entity =>
        {
            entity.Property(b => b.Name).HasColumnType($"varchar({FieldLimits.BoardName})");
            entity.Property(b => b.Description).HasColumnType($"varchar({FieldLimits.BoardDescription})");
            entity.HasOne<WorkSpace>()
                .WithMany(ws => ws.Boards)
                .HasForeignKey(b => b.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            ActorKeys(entity);
        });

        modelBuilder.Entity<List>(entity =>
        {
            entity.Property(l => l.Name).HasColumnType($"varchar({FieldLimits.ListName})");
            entity.HasIndex(l => new { l.BoardId, l.Position });
            ActorKeys(entity);
        });

        modelBuilder.Entity<Card>(entity =>
        {
            entity.Property(c => c.Title).HasColumnType($"varchar({FieldLimits.CardTitle})");
            entity.Property(c => c.Description).HasColumnType($"varchar({FieldLimits.CardDescription})");
            entity.HasIndex(c => new { c.ListId, c.Position });
            entity.HasMany(c => c.Labels)
                .WithMany(l => l.Cards)
                .UsingEntity(j => j.ToTable("CardLabels"));
            entity.HasOne<Attachment>()
                .WithMany()
                .HasForeignKey(c => c.CoverAttachmentId)
                .OnDelete(DeleteBehavior.SetNull);
            ActorKeys(entity);
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.Property(a => a.Name).HasColumnType($"varchar({FieldLimits.AttachmentName})");
            entity.Property(a => a.Url).HasColumnType($"varchar({FieldLimits.AttachmentUrl})");
            entity.Property(a => a.FileName).HasColumnType($"varchar({FieldLimits.AttachmentFileName})");
            entity.Property(a => a.MimeType).HasColumnType($"varchar({FieldLimits.AttachmentMimeType})");
            entity.Property(a => a.ObjectKey).HasColumnType($"varchar({FieldLimits.AttachmentObjectKey})");
            entity.HasOne<Card>()
                .WithMany()
                .HasForeignKey(a => a.CardId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint("CK_Attachments_Kind", KindHoldsItsColumns));
            ActorKeys(entity);
        });

        modelBuilder.Entity<PendingObjectDelete>(entity =>
        {
            entity.HasKey(p => p.ObjectKey);
            entity.Property(p => p.ObjectKey).HasColumnType($"varchar({FieldLimits.AttachmentObjectKey})");
            entity.Property(p => p.NotBefore).HasColumnType("timestamp with time zone");
            entity.HasIndex(p => p.NotBefore);
        });

        modelBuilder.Entity<Checklist>(entity =>
        {
            entity.Property(c => c.Name).HasColumnType($"varchar({FieldLimits.ChecklistName})");
            entity.HasOne<Card>()
                .WithMany()
                .HasForeignKey(c => c.CardId)
                .OnDelete(DeleteBehavior.Cascade);
            ActorKeys(entity);
        });

        modelBuilder.Entity<CheckItem>(entity =>
        {
            entity.Property(i => i.Name).HasColumnType($"varchar({FieldLimits.CheckItemName})");
            entity.HasOne<Checklist>()
                .WithMany(c => c.CheckItems)
                .HasForeignKey(i => i.ChecklistId)
                .OnDelete(DeleteBehavior.Cascade);
            ActorKeys(entity);
        });

        modelBuilder.Entity<ActivityEntry>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone");
            entity.Property(e => e.Data).HasColumnType("jsonb");
            entity.Property(e => e.Text).HasColumnType($"varchar({FieldLimits.CommentText})");
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            foreach (var place in PlaceColumns)
            {
                entity.HasIndex(place, nameof(ActivityEntry.CreatedAt), nameof(ActivityEntry.Id))
                    .IsDescending(false, true, true);
            }
            entity.HasIndex(e => e.CardId)
                .HasDatabaseName("IX_ActivityEntries_CardId_Comment")
                .HasFilter($"\"Type\" = '{nameof(ActivityType.Comment)}'");
        });

        modelBuilder.Entity<Label>(entity =>
        {
            entity.Property(l => l.Name).HasColumnType($"varchar({FieldLimits.LabelName})");
            entity.HasOne<Board>()
                .WithMany()
                .HasForeignKey(l => l.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
            ActorKeys(entity);
        });
    }

    private static readonly string[] PlaceColumns =
    [
        nameof(ActivityEntry.WorkspaceId),
        nameof(ActivityEntry.BoardId),
        nameof(ActivityEntry.ListId),
        nameof(ActivityEntry.CardId),
        nameof(ActivityEntry.FromWorkspaceId),
        nameof(ActivityEntry.FromBoardId),
        nameof(ActivityEntry.FromListId)
    ];

    private const string KindHoldsItsColumns = """
        ("Kind" = 'File'
            AND "Url" IS NULL
            AND "FileName" IS NOT NULL AND "MimeType" IS NOT NULL
            AND "Bytes" IS NOT NULL AND "ObjectKey" IS NOT NULL)
        OR ("Kind" = 'Link'
            AND "Url" IS NOT NULL
            AND "FileName" IS NULL AND "MimeType" IS NULL
            AND "Bytes" IS NULL AND "ObjectKey" IS NULL)
        """;

    private static void ActorKeys<TEntity>(EntityTypeBuilder<TEntity> entity) where TEntity : BaseEntity
    {
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
