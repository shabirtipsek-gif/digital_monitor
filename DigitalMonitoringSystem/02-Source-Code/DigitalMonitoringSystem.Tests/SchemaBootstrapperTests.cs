using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Api;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Tests;

public class SchemaBootstrapperTests
{
    private const string LegacyDevices = """
        CREATE TABLE "Devices" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Devices" PRIMARY KEY,
            "Hostname" TEXT NOT NULL,
            "IpAddress" TEXT NOT NULL,
            "Vendor" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "Cpu" INTEGER NOT NULL,
            "Memory" INTEGER NOT NULL,
            "Location" TEXT NOT NULL,
            "Platform" TEXT NOT NULL,
            "LastSeen" TEXT NOT NULL,
            "IsManaged" INTEGER NOT NULL
        )
        """;

    private const string LegacyAlerts = """
        CREATE TABLE "Alerts" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Alerts" PRIMARY KEY,
            "Title" TEXT NOT NULL,
            "Severity" TEXT NOT NULL,
            "Source" TEXT NOT NULL,
            "Timestamp" TEXT NOT NULL,
            "Acknowledged" INTEGER NOT NULL
        )
        """;

    private const string LegacyPolicies = """
        CREATE TABLE "Policies" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Policies" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "Category" TEXT NOT NULL,
            "Scope" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "AssignedDevices" INTEGER NOT NULL,
            "UpdatedAt" TEXT NOT NULL
        )
        """;

    /// <summary>A DeviceMetrics table as an earlier build would have created it, without the epoch column.</summary>
    private const string EarlierDeviceMetrics = """
        CREATE TABLE "DeviceMetrics" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_DeviceMetrics" PRIMARY KEY,
            "DeviceId" TEXT NOT NULL,
            "Cpu" INTEGER NOT NULL,
            "Memory" INTEGER NOT NULL,
            "Disk" INTEGER NOT NULL,
            "CapturedAt" TEXT NOT NULL
        )
        """;

    private static MdmDbContext LegacyDatabase(out SqliteConnectionHandle handle, params string[] tables)
    {
        handle = new SqliteConnectionHandle();
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseSqlite(handle.Connection)
            .Options;

        var context = new MdmDbContext(options);
        foreach (var table in tables)
        {
            context.Database.ExecuteSqlRaw(table);
        }

        return context;
    }

    [Fact]
    public void Initialize_CreatesTheNewTablesInALegacyDatabase()
    {
        using var context = LegacyDatabase(out var handle, LegacyDevices, LegacyAlerts, LegacyPolicies);
        using var _ = handle;

        // A database that predates the feature would fail this query outright.
        Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw("SELECT * FROM \"DeviceMetrics\""));

        context.Devices.Add(TestData.Device(id: "DEV-001", hostname: "legacy-01"));
        context.SaveChanges();

        SchemaBootstrapper.Initialize(context);

        Assert.Empty(context.DeviceMetrics.ToList());
        Assert.Empty(context.AlertRules.ToList());
        Assert.Empty(context.PolicyAssignments.ToList());
        Assert.Equal("legacy-01", context.Devices.AsNoTracking().Single().Hostname);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        using var context = LegacyDatabase(out var handle, LegacyDevices, LegacyAlerts, LegacyPolicies);
        using var _ = handle;

        SchemaBootstrapper.Initialize(context);
        SchemaBootstrapper.Initialize(context);
        SchemaBootstrapper.Initialize(context);

        context.Devices.Add(TestData.Device(id: "DEV-001", hostname: "legacy-01"));
        context.SaveChanges();

        var service = new MetricsService(context);
        var device = context.Devices.First();
        service.RecordSample(device, 50, 50, 50, DateTimeOffset.UtcNow);
        context.SaveChanges();

        Assert.Single(context.DeviceMetrics.ToList());
    }

    [Fact]
    public void Initialize_BackFillsTheEpochColumnOnAnEarlierMetricsTable()
    {
        using var context = LegacyDatabase(out var handle, LegacyDevices, LegacyAlerts, LegacyPolicies, EarlierDeviceMetrics);
        using var _ = handle;

        // The table exists but predates CapturedAtEpochMs, so CREATE TABLE IF NOT EXISTS is a no-op.
        Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw("SELECT \"CapturedAtEpochMs\" FROM \"DeviceMetrics\""));

        SchemaBootstrapper.Initialize(context);

        context.Devices.Add(TestData.Device(id: "DEV-001", hostname: "legacy-01"));
        context.DeviceMetrics.Add(TestData.Sample("DEV-001", DateTimeOffset.UtcNow, cpu: 42));
        context.SaveChanges();

        var sample = context.DeviceMetrics.AsNoTracking().Single();
        Assert.Equal(42, sample.Cpu);
        Assert.NotEqual(0, sample.CapturedAtEpochMs);
    }

    [Fact]
    public void Initialize_BackFillsTheRuleIdColumnOnAnEarlierAlertsTable()
    {
        using var context = LegacyDatabase(out var handle, LegacyDevices, LegacyAlerts, LegacyPolicies);
        using var _ = handle;

        SchemaBootstrapper.Initialize(context);

        context.Devices.Add(TestData.Device(id: "DEV-001", hostname: "legacy-01"));
        context.Alerts.Add(new AlertRecord
        {
            Id = "ALT-001",
            Title = "Legacy alert",
            Severity = "High",
            Source = "legacy-01",
            Timestamp = "2 h ago",
            Acknowledged = false
        });
        context.SaveChanges();

        var dashboard = new DashboardService(context);

        // Reading an alert from a database that predates RuleId would fail until it is back-filled.
        Assert.Equal("ALT-001", dashboard.GetAlertById("ALT-001")!.Id);
        Assert.Equal(1, dashboard.BuildDashboard().OpenAlerts);
        Assert.Empty(dashboard.BuildDashboard().Alerts[0].RuleId);
    }

    [Fact]
    public void Initialize_CreatesEveryTableTheModelExpects()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MdmDbContext>().UseSqlite(connection).Options;
        using var context = new MdmDbContext(options);

        SchemaBootstrapper.Initialize(context);

        // Ordinal ordering, so "AlertRules" sorts before "Alerts".
        var expected = new[] { "AlertRules", "Alerts", "DeviceMetrics", "Devices", "MobilePairings", "Policies", "PolicyAssignments" };
        Assert.Equal(expected, SchemaBootstrapper.ExpectedTables(context));

        // Every table has to be reachable, not just present in the model.
        foreach (var table in expected)
        {
            context.Database.ExecuteSqlRaw($"SELECT 1 FROM \"{table}\" LIMIT 1");
        }
    }

    [Fact]
    public void Apply_IsANoOpForTheInMemoryProvider()
    {
        using var context = TestData.CreateInMemory();

        // The provider has no DDL surface, so the bootstrapper has to step aside.
        SchemaBootstrapper.Initialize(context);

        Assert.Empty(context.DeviceMetrics.ToList());
    }

    internal sealed class SqliteConnectionHandle : IDisposable
    {
        public Microsoft.Data.Sqlite.SqliteConnection Connection { get; }

        public SqliteConnectionHandle()
        {
            Connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
            Connection.Open();
        }

        public void Dispose() => Connection.Dispose();
    }
}
