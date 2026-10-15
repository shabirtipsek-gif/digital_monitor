using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DigitalMonitoringSystem.Api;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Tests;

public class DashboardServiceTests
{
    [Fact]
    public void BuildDashboard_UsesDatabaseDataWhenPersistenceIsEnabled()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new MdmDbContext(options);
        context.Devices.AddRange(
            new DeviceRecord { Id = "DB-001", Hostname = "db-node-01", IpAddress = "10.0.0.21", Vendor = "Dell", Status = "Online", Cpu = 33, Memory = 44, Location = "Primary" },
            new DeviceRecord { Id = "DB-002", Hostname = "db-node-02", IpAddress = "10.0.0.22", Vendor = "Dell", Status = "Warning", Cpu = 77, Memory = 83, Location = "Replica" }
        );
        context.Alerts.AddRange(
            new AlertRecord { Id = "AL-900", Title = "DB replication lag", Severity = "High", Source = "db-node-02", Timestamp = "Now", Acknowledged = false },
            new AlertRecord { Id = "AL-901", Title = "Backup completed", Severity = "Info", Source = "db-node-01", Timestamp = "5 min ago", Acknowledged = true }
        );
        context.Policies.AddRange(
            new PolicyDefinition { Id = "POL-900", Name = "DB resiliency baseline", Category = "Compliance", Scope = "Database fleet", Status = "Active", AssignedDevices = 2, UpdatedAt = "2026-09-26T00:00:00Z" }
        );
        context.SaveChanges();

        var service = new DashboardService(context);

        DashboardSummary summary = service.BuildDashboard();

        Assert.Equal(2, summary.TotalDevices);
        Assert.Equal(1, summary.OnlineDevices);
        Assert.Equal(1, summary.OpenAlerts);
        Assert.Equal(1, summary.CriticalAlerts);
        var policy = Assert.Single(summary.Policies);
        Assert.Equal("POL-900", policy.Id);
    }

    [Fact]
    public void BuildDashboard_DoesNotSeedDemoDevicesIntoAnEmptyPersistentStore()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new MdmDbContext(options);
        var service = new DashboardService(context);

        var summary = service.BuildDashboard();

        Assert.Empty(summary.Devices);
        Assert.Empty(summary.Alerts);
        Assert.Empty(summary.Policies);
        Assert.Empty(context.Devices);
    }

    [Fact]
    public void BuildDashboard_ReturnsOperationalSummary()
    {
        var service = new DashboardService();

        DashboardSummary summary = service.BuildDashboard();

        Assert.Equal(5, summary.TotalDevices);
        Assert.Equal(5, summary.Devices.Count);
        Assert.Equal(2, summary.OpenAlerts);
        Assert.Equal(1, summary.CriticalAlerts);
        Assert.Equal(3, summary.Alerts.Count);
    }

    [Fact]
    public void GetDeviceById_ReturnsMatchingDevice()
    {
        var service = new DashboardService();

        var device = service.GetDeviceById("DEV-002");

        Assert.NotNull(device);
        Assert.Equal("DEV-002", device!.Id);
        Assert.Equal("edge-router-07", device.Hostname);
        Assert.Equal("Online", device.Status);
    }

    [Fact]
    public void GetDeviceDetails_ReturnsOpenAlertsWithoutMutatingLastSeen()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new MdmDbContext(options);
        context.Devices.Add(new DeviceRecord
        {
            Id = "DEV-800",
            Hostname = "details-node-01",
            IpAddress = "10.0.0.80",
            Vendor = "Dell",
            Status = "Warning",
            Cpu = 61,
            Memory = 72,
            Location = "Branch",
            Platform = "Windows 11",
            LastSeen = "2026-09-25T22:15:00.0000000Z",
            IsManaged = true
        });
        context.Alerts.AddRange(
            new AlertRecord { Id = "ALT-810", Title = "Fan failure predicted", Severity = "High", Source = "details-node-01", Timestamp = "Now", Acknowledged = false },
            new AlertRecord { Id = "ALT-811", Title = "Agent heartbeat missed", Severity = "Medium", Source = "details-node-01", Timestamp = "8 min ago", Acknowledged = true },
            new AlertRecord { Id = "ALT-812", Title = "Unrelated alert", Severity = "Info", Source = "other-node-02", Timestamp = "9 min ago", Acknowledged = false }
        );
        context.SaveChanges();

        var service = new DashboardService(context);

        var details = service.GetDeviceDetails("DEV-800");

        Assert.NotNull(details);
        Assert.Equal("details-node-01", details!.Hostname);
        Assert.Equal(72, details.Memory);
        var openAlert = Assert.Single(details.OpenAlerts);
        Assert.Equal("ALT-810", openAlert.Id);
        Assert.Equal("2026-09-25T22:15:00.0000000Z", context.Devices.Single(d => d.Id == "DEV-800").LastSeen);
    }

    [Fact]
    public void GetDeviceDetails_ReturnsNullForUnknownDevice()
    {
        var service = new DashboardService();

        Assert.Null(service.GetDeviceDetails("DEV-404"));
    }

    [Fact]
    public void IdentifierLookups_AreTranslatableBySqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new MdmDbContext(options);
        context.Database.EnsureCreated();
        context.Devices.Add(new DeviceRecord
        {
            Id = "DEV-777",
            Hostname = "sqlite-node-01",
            IpAddress = "10.0.0.77",
            Vendor = "HP",
            Status = "Online",
            Cpu = 20,
            Memory = 30,
            Location = "Lab",
            Platform = "Windows 11",
            LastSeen = "2026-09-20T08:00:00.0000000Z",
            IsManaged = true
        });
        context.Alerts.Add(new AlertRecord
        {
            Id = "ALT-777",
            Title = "Disk almost full",
            Severity = "Medium",
            Source = "sqlite-node-01",
            Timestamp = "Now",
            Acknowledged = false
        });
        context.Policies.Add(new PolicyDefinition
        {
            Id = "POL-777",
            Name = "Lab baseline",
            Category = "Compliance",
            Scope = "Lab devices",
            Status = "Draft",
            AssignedDevices = 1,
            UpdatedAt = "2026-09-20T08:00:00.0000000Z"
        });
        context.SaveChanges();

        var service = new DashboardService(context);

        Assert.Equal("DEV-777", service.GetDeviceById("DEV-777")?.Id);
        Assert.Equal("DEV-777", service.GetDeviceById("dev-777")?.Id);
        Assert.Equal("ALT-777", service.GetAlertById("alt-777")?.Id);
        Assert.Equal("POL-777", service.GetPolicyById("pol-777")?.Id);
        Assert.Null(service.GetDeviceById("   "));
        Assert.True(service.EnforcePolicyForDevice("dev-777", "pol-777"));
        Assert.Equal("Active", service.GetPolicyById("POL-777")?.Status);
    }

    [Fact]
    public void GetDeviceDetails_DoesNotWriteOnReadUnderSqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new MdmDbContext(options);
        context.Database.EnsureCreated();
        context.Devices.Add(new DeviceRecord
        {
            Id = "DEV-778",
            Hostname = "sqlite-node-02",
            IpAddress = "10.0.0.78",
            Vendor = "HP",
            Status = "Online",
            Cpu = 20,
            Memory = 30,
            Location = "Lab",
            Platform = "Windows 11",
            LastSeen = "2026-09-20T08:00:00.0000000Z",
            IsManaged = true
        });
        context.Alerts.Add(new AlertRecord
        {
            Id = "ALT-778",
            Title = "Pending reboot",
            Severity = "Low",
            Source = "sqlite-node-02",
            Timestamp = "Now",
            Acknowledged = false
        });
        context.SaveChanges();

        var service = new DashboardService(context);

        var details = service.GetDeviceDetails("dev-778");

        Assert.NotNull(details);
        Assert.Equal("2026-09-20T08:00:00.0000000Z", details!.LastSeen);
        Assert.Equal("ALT-778", Assert.Single(details.OpenAlerts).Id);
        Assert.Equal("2026-09-20T08:00:00.0000000Z", context.Devices.AsNoTracking().Single(d => d.Id == "DEV-778").LastSeen);
    }

    [Fact]
    public void BuildPairingCode_UsesGeneratedValue()
    {
        var response = new MobilePairingResponse
        {
            PairingCode = "MDM-PA-1001",
            Host = "192.168.1.20",
            Port = 5103,
            Fingerprint = "SHA256:LOCAL:FINGERPRINT"
        };

        Assert.Equal("MDM-PA-1001", response.PairingCode);
        Assert.Equal(5103, response.Port);
        Assert.Equal("SHA256:LOCAL:FINGERPRINT", response.Fingerprint);
    }

    [Fact]
    public void EnforcePolicyForDevice_AppliesPolicyToManagedDevice()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new MdmDbContext(options);
        context.Devices.Add(new DeviceRecord
        {
            Id = "DEV-900",
            Hostname = "policy-test-01",
            IpAddress = "10.0.0.90",
            Vendor = "Dell",
            Status = "Online",
            Cpu = 32,
            Memory = 41,
            Location = "Branch",
            Platform = "Windows 11",
            IsManaged = true
        });
        context.Policies.Add(new PolicyDefinition
        {
            Id = "POL-900",
            Name = "Zero Trust Baseline",
            Category = "Security",
            Scope = "Branch devices",
            Status = "Active",
            AssignedDevices = 1,
            UpdatedAt = "2026-09-26T00:00:00Z"
        });
        context.SaveChanges();

        var service = new DashboardService(context);

        var applied = service.EnforcePolicyForDevice("DEV-900", "POL-900");

        Assert.True(applied);
        Assert.Equal("Active", service.GetPolicyById("POL-900")!.Status);
    }

    [Fact]
    public void EnrollDevice_AddsManagedDeviceToPersistentStore()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new MdmDbContext(options);
        var service = new DashboardService(context);

        var enrolled = service.EnrollDevice(new DeviceEnrollmentRequest
        {
            Hostname = "enrolled-branch-11",
            IpAddress = "10.0.0.111",
            Vendor = "Cisco",
            Platform = "Windows 11",
            Location = "Branch"
        });

        Assert.NotNull(enrolled);
        Assert.Equal("enrolled-branch-11", enrolled!.Hostname);
        Assert.True(enrolled.IsManaged);
        Assert.Equal("Managed", enrolled.Status);
        Assert.Contains(context.Devices, d => d.Id == enrolled.Id);
    }

    [Fact]
    public void Authenticate_ReturnsRoleBasedTokenForAdministrator()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "MukonoDevelopmentSigningKey-ReplaceBeforeProduction-2026",
                ["Admin:Username"] = "admin",
                ["Admin:Password"] = "Admin@123"
            })
            .Build();

        var service = new AuthService(config);

        var auth = service.Authenticate("admin", "Admin@123");

        Assert.NotNull(auth);
        Assert.Equal("admin", auth!.Username);
        Assert.Equal("Administrator", auth.Role);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public void Authenticate_RejectsIncorrectConfiguredPassword()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "MukonoDevelopmentSigningKey-ReplaceBeforeProduction-2026",
                ["Admin:Username"] = "admin",
                ["Admin:Password"] = "Admin@123"
            })
            .Build();

        var service = new AuthService(config);

        var auth = service.Authenticate("admin", "wrong-password");

        Assert.Null(auth);
    }

    [Fact]
    public void AcknowledgeAlert_MarksAlertAsHandled()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new MdmDbContext(options);
        context.Alerts.Add(new AlertRecord
        {
            Id = "ALT-500",
            Title = "Disk pressure on vpn-gateway-01",
            Severity = "High",
            Source = "vpn-gateway-01",
            Timestamp = "Now",
            Acknowledged = false
        });
        context.SaveChanges();

        var service = new DashboardService(context);

        var acknowledged = service.AcknowledgeAlert("ALT-500");

        Assert.True(acknowledged);
        Assert.True(service.GetAlertById("ALT-500")!.Acknowledged);
    }

    [Fact]
    public void GetSystemHealth_ReturnsOperationalSnapshot()
    {
        var service = new DashboardService();

        var snapshot = service.GetSystemHealth();

        Assert.Equal("DigitalMonitoringSystem.Api", snapshot.ServiceName);
        Assert.Equal("Degraded", snapshot.Status);
        Assert.Equal(5, snapshot.Summary.TotalDevices);
        Assert.Equal(2, snapshot.Summary.OpenAlerts);
        Assert.True(snapshot.LastSyncUtc <= DateTimeOffset.UtcNow);
    }
}
