using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Api;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Tests;

public class DeviceQueryTests
{
    private static void Seed(MdmDbContext context)
    {
        context.Devices.AddRange(
            new DeviceRecord { Id = "DEV-001", Hostname = "core-switch-01", IpAddress = "10.0.0.11", Vendor = "Cisco", Status = "Online", Location = "Core", Platform = "Windows Server", Cpu = 34, Memory = 58, IsManaged = true, LastSeen = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow) },
            new DeviceRecord { Id = "DEV-002", Hostname = "edge-router-07", IpAddress = "10.0.0.17", Vendor = "Juniper", Status = "Online", Location = "Edge", Platform = "Linux", Cpu = 46, Memory = 62, IsManaged = true, LastSeen = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow) },
            new DeviceRecord { Id = "DEV-003", Hostname = "wireless-ap-05", IpAddress = "10.0.0.25", Vendor = "Aruba", Status = "Warning", Location = "Wireless", Platform = "Network Appliance", Cpu = 69, Memory = 72, IsManaged = true, LastSeen = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow) },
            new DeviceRecord { Id = "DEV-004", Hostname = "mobile-bridge-02", IpAddress = "10.0.0.52", Vendor = "Dell", Status = "Offline", Location = "Mobile", Platform = "Windows 11", Cpu = 0, Memory = 0, IsManaged = false, LastSeen = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow) });
        context.Policies.AddRange(
            new PolicyDefinition { Id = "POL-100", Name = "Baseline", Category = "Compliance", Scope = "Fleet", Status = "Active", AssignedDevices = 3, UpdatedAt = "2026-09-26T00:00:00.000Z" },
            new PolicyDefinition { Id = "POL-101", Name = "Perimeter guardrail", Category = "Security", Scope = "Perimeter", Status = "Active", AssignedDevices = 1, UpdatedAt = "2026-09-26T00:00:00.000Z" });
        context.SaveChanges();
    }

    [Fact]
    public void QueryDevices_ReturnsEverythingSortedByHostnameByDefault()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        var result = service.QueryDevices(new DeviceQuery());

        Assert.Equal(4, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(
            new[] { "core-switch-01", "edge-router-07", "mobile-bridge-02", "wireless-ap-05" },
            result.Items.Select(d => d.Hostname).ToArray());
    }

    [Fact]
    public void QueryDevices_SortsDescendingWhenAsked()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        var result = service.QueryDevices(new DeviceQuery { SortBy = "Hostname", Descending = true });

        Assert.Equal(
            new[] { "wireless-ap-05", "mobile-bridge-02", "edge-router-07", "core-switch-01" },
            result.Items.Select(d => d.Hostname).ToArray());
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Memory")]
    [InlineData("Status")]
    [InlineData("Location")]
    [InlineData("Vendor")]
    [InlineData("Platform")]
    [InlineData("IpAddress")]
    [InlineData("LastSeen")]
    [InlineData("Id")]
    [InlineData("SomethingUnrecognised")]
    public void QueryDevices_AcceptsEverySortKey(string sortBy)
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        var result = service.QueryDevices(new DeviceQuery { SortBy = sortBy, Descending = true });

        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public void QueryDevices_SortsNumericColumnsNumerically()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        var result = service.QueryDevices(new DeviceQuery { SortBy = "Cpu" });

        // Cpu 0, 34, 46, 69 - a text sort would put "69" before "34".
        Assert.Equal(new[] { 0, 34, 46, 69 }, result.Items.Select(d => d.Cpu).ToArray());
    }

    [Fact]
    public void QueryDevices_MatchesTheFreeTextSearchAcrossFields()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        Assert.Equal("DEV-002", Assert.Single(service.QueryDevices(new DeviceQuery { Search = "juniper" }).Items).Id);
        Assert.Equal("DEV-003", Assert.Single(service.QueryDevices(new DeviceQuery { Search = "wireless-ap" }).Items).Id);
        Assert.Equal("DEV-004", Assert.Single(service.QueryDevices(new DeviceQuery { Search = "10.0.0.52" }).Items).Id);
        Assert.Equal(4, service.QueryDevices(new DeviceQuery { Search = "   " }).Total);
    }

    [Fact]
    public void QueryDevices_FiltersOnStatusLocationPlatformAndVendor()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        Assert.Equal(2, service.QueryDevices(new DeviceQuery { Status = "Online" }).Total);
        Assert.Equal(2, service.QueryDevices(new DeviceQuery { Status = "online" }).Total);
        Assert.Equal(1, service.QueryDevices(new DeviceQuery { Location = "Edge" }).Total);
        Assert.Equal(1, service.QueryDevices(new DeviceQuery { Platform = "Linux" }).Total);
        Assert.Equal(1, service.QueryDevices(new DeviceQuery { Vendor = "aruba" }).Total);
        Assert.Equal(0, service.QueryDevices(new DeviceQuery { Status = "Retired" }).Total);
    }

    [Fact]
    public void QueryDevices_FiltersOnManagedState()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        var result = service.QueryDevices(new DeviceQuery { ManagedOnly = true });

        Assert.Equal(3, result.Total);
        Assert.DoesNotContain(result.Items, device => device.Id == "DEV-004");
    }

    [Fact]
    public void QueryDevices_PagesThroughTheFleet()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        var first = service.QueryDevices(new DeviceQuery { Page = 1, PageSize = 3 });
        var second = service.QueryDevices(new DeviceQuery { Page = 2, PageSize = 3 });
        var third = service.QueryDevices(new DeviceQuery { Page = 3, PageSize = 3 });

        Assert.Equal(
            new[] { "core-switch-01", "edge-router-07", "mobile-bridge-02" },
            first.Items.Select(device => device.Hostname).ToArray());
        Assert.Equal(2, first.PageCount);
        Assert.Empty(third.Items);
        Assert.Equal(4, first.Total);
        Assert.Equal("wireless-ap-05", Assert.Single(second.Items).Hostname);
    }

    [Fact]
    public void QueryDevices_IsStableAcrossCallsForEqualSortKeys()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        for (var index = 0; index < 6; index++)
        {
            context.Devices.Add(new DeviceRecord
            {
                Id = $"DEV-9{index}",
                Hostname = "replica",
                IpAddress = $"10.0.1.{index}",
                Status = "Online",
                Location = "Lab",
                Platform = "Linux",
                IsManaged = true
            });
        }
        context.SaveChanges();
        var service = new DashboardService(context);

        var firstPage = service.QueryDevices(new DeviceQuery { SortBy = "Location", Page = 2, PageSize = 4 }).Items;
        var secondPage = service.QueryDevices(new DeviceQuery { SortBy = "Location", Page = 2, PageSize = 4 }).Items;

        Assert.Equal(firstPage.Select(d => d.Id), secondPage.Select(d => d.Id));
    }

    [Fact]
    public void QueryDevices_ClampsThePageSize()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var service = new DashboardService(context);

        Assert.Equal(500, service.QueryDevices(new DeviceQuery { PageSize = 5000 }).PageSize);
        Assert.Equal(50, service.QueryDevices(new DeviceQuery { PageSize = 0 }).PageSize);
        Assert.Equal(1, service.QueryDevices(new DeviceQuery { Page = 0 }).Page);
    }

    [Fact]
    public void RemoveDevice_DropsTheDeviceItsMetricsAndItsAssignments()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var metrics = new MetricsService(context);
        var device = context.Devices.Single(d => d.Id == "DEV-001");
        metrics.RecordSample(device, 30, 30, 30, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var policies = new PolicyService(context);
        policies.Assign("DEV-001", "POL-100", "admin");
        policies.Assign("DEV-002", "POL-100", "admin");
        Assert.Equal(2, policies.GetPolicyById("POL-100")!.AssignedDevices);

        var service = new DashboardService(context);

        Assert.True(service.RemoveDevice("DEV-001"));

        Assert.Null(service.GetDeviceById("DEV-001"));
        Assert.Empty(context.DeviceMetrics.ToList());
        Assert.Empty(policies.GetAssignmentsForDevice("DEV-001"));
        // The surviving device keeps its own assignment.
        Assert.Equal(1, policies.GetPolicyById("POL-100")!.AssignedDevices);
    }

    [Fact]
    public void RemoveDevice_ReturnsFalseForAnUnknownDevice()
    {
        using var context = TestData.CreateInMemory();
        var service = new DashboardService(context);

        Assert.False(service.RemoveDevice("DEV-404"));
        Assert.False(service.RemoveDevice("   "));
    }

    [Fact]
    public void RecordHeartbeat_StoresASampleAndRefreshesTheDevice()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var metrics = new MetricsService(context);
        var service = new DashboardService(context);

        var (sample, response) = service.RecordHeartbeat("DEV-003", new DeviceHeartbeatRequest
        {
            Cpu = 81,
            Memory = 77,
            Disk = 66,
            AgentVersion = "1.4.0"
        }, metrics);

        Assert.NotNull(sample);
        Assert.NotNull(response);
        Assert.Equal("DEV-003", response!.DeviceId);
        Assert.Equal("Online", response.Status);
        Assert.Equal(1, response.SampleCount);
        Assert.Equal(81, service.GetDeviceById("DEV-003")!.Cpu);
        Assert.Equal(77, service.GetDeviceById("DEV-003")!.Memory);
    }

    [Fact]
    public void RecordHeartbeat_ClampsReportedValuesAndBringsAnOfflineDeviceBack()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var metrics = new MetricsService(context);
        var service = new DashboardService(context);

        service.RecordHeartbeat("DEV-004", new DeviceHeartbeatRequest { Cpu = 400, Memory = -5, Disk = 10 }, metrics);

        var device = service.GetDeviceById("DEV-004")!;
        Assert.Equal(100, device.Cpu);
        Assert.Equal(0, device.Memory);
        Assert.Equal("Online", device.Status);
    }

    [Fact]
    public void RecordHeartbeat_ReturnsNullsForAnUnknownDevice()
    {
        using var context = TestData.CreateInMemory();
        var service = new DashboardService(context);

        var (sample, response) = service.RecordHeartbeat("DEV-404", new DeviceHeartbeatRequest { Cpu = 10 }, new MetricsService(context));

        Assert.Null(sample);
        Assert.Null(response);
    }

    [Fact]
    public void RecordHeartbeat_CountsEveryStoredSample()
    {
        using var context = TestData.CreateInMemory();
        Seed(context);
        var metrics = new MetricsService(context);
        var service = new DashboardService(context);

        service.RecordHeartbeat("DEV-001", new DeviceHeartbeatRequest { Cpu = 10 }, metrics);
        var (_, second) = service.RecordHeartbeat("DEV-001", new DeviceHeartbeatRequest { Cpu = 20 }, metrics);

        Assert.Equal(2, second!.SampleCount);
    }
}
