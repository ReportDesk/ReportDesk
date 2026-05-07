using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using ReportSystem.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options){ }

    public DbSet<MasterReport> MasterReports { get; set; }
    public DbSet<MasterFollowUp> MasterFollowUps { get; set; }
    public DbSet<MasterUser> MasterUsers { get; set; }
    public DbSet<MasterMachine> MasterMachines { get; set; }
    public DbSet<Attachment> Attachments { get; set; }
}
