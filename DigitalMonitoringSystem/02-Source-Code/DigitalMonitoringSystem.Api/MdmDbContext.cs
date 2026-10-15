using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class MdmDbContext : DbContext
{
    public MdmDbContext(DbContextOptions<MdmDbContext> options)
        : base(options)
    {
    }

    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();
    public DbSet<AlertRecord> Alerts => Set<AlertRecord>();
    public DbSet<PolicyDefinition> Policies => Set<PolicyDefinition>();
    public DbSet<DeviceMetricSample> DeviceMetrics => Set<DeviceMetricSample>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<DevicePolicyAssignment> PolicyAssignments => Set<DevicePolicyAssignment>();
    public DbSet<MobilePairingRecord> MobilePairings => Set<MobilePairingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).IsRequired();
            entity.Property(x => x.Hostname).IsRequired();
            entity.Property(x => x.IpAddress).IsRequired();
        });

        modelBuilder.Entity<AlertRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).IsRequired();
            entity.Property(x => x.Title).IsRequired();
        });

        modelBuilder.Entity<PolicyDefinition>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).IsRequired();
            entity.Property(x => x.Name).IsRequired();
        });

        modelBuilder.Entity<DeviceMetricSample>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).IsRequired();
            entity.Property(x => x.DeviceId).IsRequired();
            entity.HasIndex(x => new { x.DeviceId, x.CapturedAtEpochMs });
        });

        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).IsRequired();
            entity.Property(x => x.Name).IsRequired();
        });

        modelBuilder.Entity<MobilePairingRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).IsRequired();
            entity.Property(x => x.PairingCode).IsRequired();
            entity.Property(x => x.DeviceName).IsRequired();
            entity.Property(x => x.EnrollmentToken).IsRequired();
            entity.HasIndex(x => x.PairingCode).IsUnique();
        });

        modelBuilder.Entity<DevicePolicyAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).IsRequired();
            entity.Property(x => x.DeviceId).IsRequired();
            entity.Property(x => x.PolicyId).IsRequired();
            entity.HasIndex(x => new { x.PolicyId, x.DeviceId }).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
