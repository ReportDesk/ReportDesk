using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options){ }

    public DbSet<MasterReport> MasterReports { get; set; }
    public DbSet<MasterFollowUp> MasterFollowUps { get; set; }
    public DbSet<MasterUser> MasterUsers { get; set; }
    public DbSet<MasterMachine> MasterMachines { get; set; }
    public DbSet<Attachment> Attachments { get; set; }
    public DbSet<PendingR2ObjectDeletion> PendingR2ObjectDeletions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MasterUser>()
            .Property(x => x.CreatedDate)
            .HasColumnType("timestamp without time zone");

        modelBuilder.Entity<MasterFollowUp>()
            .HasIndex(x => x.IdempotencyClaim)
            .IsUnique();

        modelBuilder.Entity<PendingR2ObjectDeletion>()
            .HasIndex(x => x.ObjectKey)
            .IsUnique();
    }
}
