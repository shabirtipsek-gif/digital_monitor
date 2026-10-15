using System.Globalization;
using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Api;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Tests;

public class MetricsServiceTests
{
    [Fact]
    public void RecordSample_RefreshesDeviceAndStoresSample()
    {
        using var context = TestData.CreateInMemory();
        var device = TestData.Device(cpu: 5, memory: 5);
        context.Devices.Add(device);
        context.SaveChanges();

        var service = new MetricsService(context);
        var capturedAt = DateTimeOffset.UtcNow;

        var sample = service.RecordSample(device, cpu: 64, memory: 71, disk: 55, capturedAt);
        context.SaveChanges();

        Assert.Equal(device.Id, sample.DeviceId);
        Assert.Equal(64, sample.Cpu);
        Assert.Equal(71, sample.Memory);
        Assert.Equal(55, sample.Disk);
        Assert.Equal(capturedAt.ToUnixTimeMilliseconds(), sample.CapturedAtEpochMs);

        var reloaded = context.Devices.AsNoTracking().Single(d => d.Id == device.Id);
        Assert.Equal(64, reloaded.Cpu);
        Assert.Equal(71, reloaded.Memory);
        Assert.Equal("Online", reloaded.Status);
        Assert.Equal(sample.CapturedAt, reloaded.LastSeen);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void RecordSample_ClampsPercentagesToHundred(int reported, int expected)
    {
        using var context = TestData.CreateInMemory();
        var device = TestData.Device();
        context.Devices.Add(device);
        context.SaveChanges();

        var sample = new MetricsService(context).RecordSample(device, reported, reported, reported, DateTimeOffset.UtcNow);

        Assert.Equal(expected, sample.Cpu);
        Assert.Equal(expected, sample.Memory);
        Assert.Equal(expected, sample.Disk);
    }

    [Fact]
    public void GetSamples_ReturnsOnlyTheRequestedWindowInChronologicalOrder()
    {
        using var context = TestData.CreateInMemory();
        var now = DateTimeOffset.UtcNow;
        var device = TestData.Device();
        context.Devices.Add(device);
        context.DeviceMetrics.AddRange(
            TestData.Sample(device.Id, now.AddMinutes(-90), cpu: 91),
            TestData.Sample(device.Id, now.AddMinutes(-30), cpu: 10),
            TestData.Sample(device.Id, now.AddMinutes(-20), cpu: 20),
            TestData.Sample(device.Id, now.AddMinutes(-10), cpu: 30));
        context.SaveChanges();

        var service = new MetricsService(context);
        var samples = service.GetSamples(device.Id, windowMinutes: 60, maxSamples: 100);

        Assert.Equal(3, samples.Count);
        Assert.Equal(new[] { 10, 20, 30 }, samples.Select(s => s.Cpu).ToArray());
    }

    [Fact]
    public void GetSamples_HonoursMaxSamplesByKeepingTheMostRecent()
    {
        using var context = TestData.CreateInMemory();
        var now = DateTimeOffset.UtcNow;
        var device = TestData.Device();
        context.Devices.Add(device);
        context.DeviceMetrics.AddRange(Enumerable.Range(0, 10)
            .Select(index => TestData.Sample(device.Id, now.AddMinutes(-10 + index), cpu: index)));
        context.SaveChanges();

        var samples = new MetricsService(context).GetSamples(device.Id, windowMinutes: 60, maxSamples: 3);

        Assert.Equal(new[] { 7, 8, 9 }, samples.Select(s => s.Cpu).ToArray());
    }

    [Fact]
    public void GetWindow_AveragesAndPeaksTheSamples()
    {
        using var context = TestData.CreateInMemory();
        var now = DateTimeOffset.UtcNow;
        var device = TestData.Device();
        context.Devices.Add(device);
        context.DeviceMetrics.AddRange(
            TestData.Sample(device.Id, now.AddMinutes(-20), cpu: 10, memory: 20),
            TestData.Sample(device.Id, now.AddMinutes(-10), cpu: 50, memory: 60),
            TestData.Sample(device.Id, now.AddMinutes(-5), cpu: 90, memory: 40));
        context.SaveChanges();

        var window = new MetricsService(context).GetWindow(device.Id, windowMinutes: 60);

        Assert.Equal(3, window.SampleCount);
        Assert.Equal(50.0, window.AvgCpu);
        Assert.Equal(40.0, window.AvgMemory);
        Assert.Equal(90, window.PeakCpu);
        Assert.Equal(60, window.PeakMemory);
    }

    [Fact]
    public void GetWindow_ReturnsZeroesForADeviceThatHasNeverReported()
    {
        using var context = TestData.CreateInMemory();
        var device = TestData.Device();
        context.Devices.Add(device);
        context.SaveChanges();

        var window = new MetricsService(context).GetWindow(device.Id, windowMinutes: 60);

        Assert.Equal(0, window.SampleCount);
        Assert.Equal(0, window.AvgCpu);
        Assert.Equal(0, window.PeakCpu);
        Assert.Empty(window.Samples);
    }

    [Fact]
    public void GetSamples_TranslatesToSqliteWithTheEpochColumn()
    {
        using var context = TestData.CreateSqlite(out var connection);
        var now = DateTimeOffset.UtcNow;
        var device = TestData.Device();
        context.Devices.Add(device);
        context.DeviceMetrics.AddRange(
            TestData.Sample(device.Id, now.AddHours(-3), cpu: 99),
            TestData.Sample(device.Id, now.AddMinutes(-15), cpu: 42));
        context.SaveChanges();

        var samples = new MetricsService(context).GetSamples(device.Id, windowMinutes: 60, maxSamples: 100);

        Assert.Equal(42, Assert.Single(samples).Cpu);
        connection.Dispose();
    }

    [Fact]
    public void Prune_RemovesSamplesOlderThanTheRetentionWindow()
    {
        using var context = TestData.CreateInMemory();
        var now = DateTimeOffset.UtcNow;
        var device = TestData.Device();
        context.Devices.Add(device);
        context.DeviceMetrics.AddRange(
            TestData.Sample(device.Id, now.AddDays(-9), cpu: 1),
            TestData.Sample(device.Id, now.AddHours(-2), cpu: 2),
            TestData.Sample(device.Id, now.AddMinutes(-5), cpu: 3));
        context.SaveChanges();

        var removed = new MetricsService(context).Prune(retentionMinutes: 7 * 24 * 60);

        Assert.Equal(1, removed);
        Assert.Equal(2, context.DeviceMetrics.Count());
    }

    [Fact]
    public void Prune_IsANoOpWhenEverythingIsInsideTheWindow()
    {
        using var context = TestData.CreateInMemory();
        var device = TestData.Device();
        context.Devices.Add(device);
        context.DeviceMetrics.Add(TestData.Sample(device.Id, DateTimeOffset.UtcNow, cpu: 5));
        context.SaveChanges();

        Assert.Equal(0, new MetricsService(context).Prune(retentionMinutes: 60));
    }

    [Theory]
    [InlineData(0, MetricsService.DefaultWindowMinutes)]
    [InlineData(-5, MetricsService.DefaultWindowMinutes)]
    [InlineData(30, 30)]
    [InlineData(999999, MetricsService.MaxWindowMinutes)]
    public void ClampWindow_BoundsTheRequestedWindow(int requested, int expected)
    {
        Assert.Equal(expected, MetricsService.ClampWindow(requested));
    }

    [Fact]
    public void TryParseTimestamp_AcceptsTheLayoutsAlreadyStoredInTheDatabase()
    {
        // The seed data writes "O" timestamps while new samples use the shorter round-trip layout,
        // so both have to land on the same instant.
        var expected = DateTimeOffset.Parse(
            "2026-09-26T20:15:30.0000000Z",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        Assert.Equal(expected, MetricsService.TryParseTimestamp("2026-09-26T20:15:30.0000000Z"));
        Assert.Equal(expected, MetricsService.TryParseTimestamp("2026-09-26T20:15:30.000Z"));
        Assert.Null(MetricsService.TryParseTimestamp("not-a-timestamp"));
        Assert.Null(MetricsService.TryParseTimestamp(null));
        Assert.Null(MetricsService.TryParseTimestamp("   "));
    }

    [Fact]
    public void GetDevicesOfflineFor_TreatsStaleMissingAndUnreadableTimestampsAsOffline()
    {
        using var context = TestData.CreateInMemory();
        var now = DateTimeOffset.UtcNow;

        var blank = TestData.Device(id: "DEV-BLANK", hostname: "blank-01");
        blank.LastSeen = string.Empty;
        var junk = TestData.Device(id: "DEV-JUNK", hostname: "junk-01");
        junk.LastSeen = "yesterday";

        context.Devices.AddRange(
            TestData.Device(id: "DEV-FRESH", hostname: "fresh-01", lastSeen: now.AddMinutes(-2)),
            TestData.Device(id: "DEV-STALE", hostname: "stale-01", lastSeen: now.AddMinutes(-45)),
            blank,
            junk);
        context.SaveChanges();

        var offline = new MetricsService(context).GetDevicesOfflineFor(now, minutes: 15);

        Assert.Equal(
            new[] { "DEV-BLANK", "DEV-JUNK", "DEV-STALE" },
            offline.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }
}
