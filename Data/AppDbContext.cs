using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<MasterReport> MasterReports => Set<MasterReport>();
    public DbSet<MasterMachine> MasterMachines => Set<MasterMachine>();
    public DbSet<MasterFollowUp> MasterFollowUps => Set<MasterFollowUp>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<MasterUser> MasterUsers => Set<MasterUser>();
}
