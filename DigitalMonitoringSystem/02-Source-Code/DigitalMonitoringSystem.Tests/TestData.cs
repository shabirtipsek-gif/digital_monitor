using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Api;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Tests;

internal static class TestData
{
    public static MdmDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MdmDbContext(options);
    }

    public static MdmDbContext CreateSqlite(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new MdmDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static DeviceRecord Device(
        string id = "DEV-001",
        string hostname = "test-node-01",
        string status = "Online",
        bool isManaged = true,
        DateTimeOffset? lastSeen = null,
        int cpu = 20,
        int memory = 30) => new()
        {
            Id = id,
            Hostname = hostname,
            IpAddress = "10.0.0.1",
            Vendor = "Dell",
            Status = status,
            Cpu = cpu,
            Memory = memory,
            Location = "Branch",
            Platform = "Windows 11",
            IsManaged = isManaged,
            LastSeen = MetricsService.FormatTimestamp(lastSeen ?? DateTimeOffset.UtcNow)
        };

    public static DeviceMetricSample Sample(string deviceId, DateTimeOffset capturedAt, int cpu, int memory = 40, int disk = 50) => new()
    {
        Id = $"MET-{Guid.NewGuid():N}".ToUpperInvariant(),
        DeviceId = deviceId,
        Cpu = cpu,
        Memory = memory,
        Disk = disk,
        CapturedAt = MetricsService.FormatTimestamp(capturedAt),
        CapturedAtEpochMs = capturedAt.ToUnixTimeMilliseconds()
    };

    /// <summary>
    /// The service seeds example rules on first use. Tests that assert on evaluation results clear
    /// them so only the rule under test is in play.
    /// </summary>
    public static void ClearSeededRules(MdmDbContext context)
    {
        context.AlertRules.RemoveRange(context.AlertRules.ToList());
        context.SaveChanges();
    }
}
