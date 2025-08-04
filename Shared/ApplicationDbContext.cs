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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
