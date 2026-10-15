using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class DashboardService
{
    private readonly MdmDbContext _context;

    public DashboardService()
        : this(CreateFallbackContext())
    {
    }

    public DashboardService(MdmDbContext context)
    {
        _context = context;
    }

    private static MdmDbContext CreateFallbackContext()
    {
        var options = new DbContextOptionsBuilder<MdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new MdmDbContext(options);
        context.Devices.AddRange(new List<DeviceRecord>
        {
            new() { Id = "DEV-001", Hostname = "core-switch-01", IpAddress = "10.0.0.11", Vendor = "Cisco", Status = "Online", Location = "Core", Cpu = 34, Memory = 58, Platform = "Windows Server", LastSeen = DateTime.UtcNow.AddMinutes(-4).ToString("O"), IsManaged = true },
            new() { Id = "DEV-002", Hostname = "edge-router-07", IpAddress = "10.0.0.17", Vendor = "Juniper", Status = "Online", Location = "Edge", Cpu = 46, Memory = 62, Platform = "Linux", LastSeen = DateTime.UtcNow.AddMinutes(-9).ToString("O"), IsManaged = true },
            new() { Id = "DEV-003", Hostname = "wireless-ap-05", IpAddress = "10.0.0.25", Vendor = "Aruba", Status = "Warning", Location = "Wireless", Cpu = 69, Memory = 72, Platform = "Network Appliance", LastSeen = DateTime.UtcNow.AddMinutes(-2).ToString("O"), IsManaged = true },
            new() { Id = "DEV-004", Hostname = "vpn-gateway-01", IpAddress = "10.0.0.41", Vendor = "Fortinet", Status = "Online", Location = "Perimeter", Cpu = 41, Memory = 54, Platform = "Firewall", LastSeen = DateTime.UtcNow.AddMinutes(-11).ToString("O"), IsManaged = true },
            new() { Id = "DEV-005", Hostname = "mobile-bridge-02", IpAddress = "10.0.0.52", Vendor = "Dell", Status = "Offline", Location = "Mobile", Cpu = 0, Memory = 0, Platform = "Windows 11", LastSeen = DateTime.UtcNow.AddHours(-2).ToString("O"), IsManaged = false }
        });
        context.Alerts.AddRange(new List<AlertRecord>
        {
            new() { Id = "ALT-101", Title = "Core switch temperature elevated", Severity = "High", Source = "core-switch-01", Timestamp = "Now", Acknowledged = false },
            new() { Id = "ALT-202", Title = "Wireless SSID drift detected", Severity = "Medium", Source = "wireless-ap-05", Timestamp = "2 min ago", Acknowledged = true },
            new() { Id = "ALT-303", Title = "VPN gateway certificate nearing expiry", Severity = "Medium", Source = "vpn-gateway-01", Timestamp = "12 min ago", Acknowledged = false }
        });
        context.Policies.AddRange(new List<PolicyDefinition>
        {
            new() { Id = "POL-100", Name = "Standard Endpoint Baseline", Category = "Compliance", Scope = "All Windows devices", Status = "Active", AssignedDevices = 18, UpdatedAt = DateTime.UtcNow.AddHours(-2).ToString("O") },
            new() { Id = "POL-101", Name = "Network Access Guardrail", Category = "Security", Scope = "Perimeter devices", Status = "Active", AssignedDevices = 7, UpdatedAt = DateTime.UtcNow.AddDays(-1).ToString("O") },
            new() { Id = "POL-102", Name = "Patch Enforcement Window", Category = "Operations", Scope = "Managed fleet", Status = "Scheduled", AssignedDevices = 42, UpdatedAt = DateTime.UtcNow.AddHours(-8).ToString("O") }
        });
        context.SaveChanges();
        return context;
    }

    public DashboardSummary BuildDashboard()
    {
        var devices = _context.Devices.AsNoTracking().Select(d => new DeviceSummary
        {
            Id = d.Id,
            Hostname = d.Hostname,
            IpAddress = d.IpAddress,
            Status = d.Status,
            Cpu = d.Cpu,
            Memory = d.Memory,
            Location = d.Location
        }).ToList();

        var alerts = _context.Alerts.AsNoTracking().Select(a => new AlertSummary
        {
            Id = a.Id,
            Title = a.Title,
            Severity = a.Severity,
            Source = a.Source,
            Timestamp = a.Timestamp,
            Acknowledged = a.Acknowledged,
            RuleId = a.RuleId
        }).ToList();

        var policies = _context.Policies.AsNoTracking().Select(p => new PolicyDefinition
        {
            Id = p.Id,
            Name = p.Name,
            Category = p.Category,
            Scope = p.Scope,
            Status = p.Status,
            AssignedDevices = p.AssignedDevices,
            UpdatedAt = p.UpdatedAt
        }).ToList();

        return new DashboardSummary
        {
            TotalDevices = devices.Count,
            OnlineDevices = devices.Count(d => d.Status == "Online"),
            OpenAlerts = alerts.Count(a => !a.Acknowledged),
            CriticalAlerts = alerts.Count(a => a.Severity == "High"),
            AvgCpu = devices.Count == 0 ? 0 : Math.Round(devices.Average(d => d.Cpu), 1),
            AvgMemory = devices.Count == 0 ? 0 : Math.Round(devices.Average(d => d.Memory), 1),
            Devices = devices,
            Alerts = alerts,
            Policies = policies
        };
    }

    public SystemHealthSnapshot GetSystemHealth()
    {
        var summary = BuildDashboard();
        return new SystemHealthSnapshot
        {
            ServiceName = "DigitalMonitoringSystem.Api",
            Status = summary.OpenAlerts == 0 ? "Healthy" : "Degraded",
            LastSyncUtc = DateTimeOffset.UtcNow,
            Summary = summary
        };
    }

    public DeviceRecord? GetDeviceById(string id)
    {
        return FindById(_context.Devices, id);
    }

    public DeviceDetails? GetDeviceDetails(string id)
    {
        var device = GetDeviceById(id);
        if (device is null)
        {
            return null;
        }

        var normalizedHostname = device.Hostname.ToLowerInvariant();

        var openAlerts = _context.Alerts.AsNoTracking()
            .Where(alert => !alert.Acknowledged
                && alert.Source.ToLower() == normalizedHostname)
            .Select(alert => new AlertSummary
            {
                Id = alert.Id,
                Title = alert.Title,
                Severity = alert.Severity,
                Source = alert.Source,
                Timestamp = alert.Timestamp,
                Acknowledged = alert.Acknowledged,
                RuleId = alert.RuleId
            })
            .ToList();

        return new DeviceDetails
        {
            Id = device.Id,
            Hostname = device.Hostname,
            IpAddress = device.IpAddress,
            Vendor = device.Vendor,
            Status = device.Status,
            Cpu = device.Cpu,
            Memory = device.Memory,
            Location = device.Location,
            Platform = device.Platform,
            LastSeen = device.LastSeen,
            IsManaged = device.IsManaged,
            OpenAlerts = openAlerts
        };
    }

    public AlertRecord? GetAlertById(string id)
    {
        return FindById(_context.Alerts, id);
    }

    public PolicyDefinition? GetPolicyById(string id)
    {
        return FindById(_context.Policies, id);
    }

    private static TEntity? FindById<TEntity>(DbSet<TEntity> set, string id) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var candidate = id.Trim();
        var normalized = candidate.ToLowerInvariant();

        return set.FirstOrDefault(entity => EF.Property<string>(entity, "Id") == candidate)
            ?? set.FirstOrDefault(entity => EF.Property<string>(entity, "Id").ToLower() == normalized);
    }

    public bool EnforcePolicyForDevice(string deviceId, string policyId)
    {
        var device = GetDeviceById(deviceId);
        var policy = GetPolicyById(policyId);

        if (device is null || policy is null)
        {
            return false;
        }

        if (!device.IsManaged)
        {
            return false;
        }

        policy.Status = "Active";
        policy.AssignedDevices = Math.Max(1, policy.AssignedDevices);
        device.Status = device.Status == "Offline" ? "Warning" : device.Status;

        _context.SaveChanges();
        return true;
    }

    public bool AcknowledgeAlert(string alertId)
    {
        var alert = GetAlertById(alertId);
        if (alert is null)
        {
            return false;
        }

        alert.Acknowledged = true;
        _context.SaveChanges();
        return true;
    }

    public DeviceEnrollmentResponse? EnrollDevice(DeviceEnrollmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Hostname) || string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return null;
        }

        var hostname = request.Hostname.Trim();
        var normalizedHostname = hostname.ToLower();

        // A computer that is enrolled again keeps its device id and history; it only gets its
        // details refreshed and a new token, so the fleet never lists the same machine twice.
        var existing = _context.Devices.FirstOrDefault(d => d.Hostname.ToLower() == normalizedHostname);
        if (existing is not null)
        {
            existing.IpAddress = request.IpAddress.Trim();
            if (!string.IsNullOrWhiteSpace(request.Vendor)) existing.Vendor = request.Vendor.Trim();
            if (!string.IsNullOrWhiteSpace(request.Platform)) existing.Platform = request.Platform.Trim();
            if (!string.IsNullOrWhiteSpace(request.Location)) existing.Location = request.Location.Trim();
            existing.IsManaged = true;
            _context.SaveChanges();

            return new DeviceEnrollmentResponse
            {
                Id = existing.Id,
                Hostname = existing.Hostname,
                Status = existing.Status,
                IsManaged = existing.IsManaged,
                PolicyState = "Pending",
                ReEnrolled = true
            };
        }

        var id = $"DEV-{Guid.NewGuid():N}".ToUpperInvariant();
        var device = new DeviceRecord
        {
            Id = id,
            Hostname = hostname,
            IpAddress = request.IpAddress.Trim(),
            Vendor = request.Vendor?.Trim() ?? string.Empty,
            Platform = request.Platform?.Trim() ?? string.Empty,
            Location = request.Location?.Trim() ?? string.Empty,
            Status = "Managed",
            IsManaged = true,
            Cpu = 0,
            Memory = 0,
            LastSeen = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow)
        };

        _context.Devices.Add(device);
        _context.SaveChanges();

        return new DeviceEnrollmentResponse
        {
            Id = device.Id,
            Hostname = device.Hostname,
            Status = device.Status,
            IsManaged = device.IsManaged,
            PolicyState = "Pending"
        };
    }

    public IReadOnlyList<DeviceRecord> GetDevices() => _context.Devices.AsNoTracking().ToList().AsReadOnly();

    public IReadOnlyList<AlertRecord> GetAlerts() => _context.Alerts.AsNoTracking().ToList().AsReadOnly();

    public IReadOnlyList<PolicyDefinition> GetPolicies() => _context.Policies.AsNoTracking().ToList().AsReadOnly();

    /// <summary>
    /// Filtered, sorted and paged device inventory. Filtering happens in memory because LastSeen
    /// and status are free text, and a division-sized fleet is small enough that materialising it
    /// is cheaper than building the same predicate into SQL.
    /// </summary>
    public PagedResult<DeviceRecord> QueryDevices(DeviceQuery query)
    {
        var search = (query.Search ?? string.Empty).Trim().ToLowerInvariant();
        var status = (query.Status ?? string.Empty).Trim();
        var location = (query.Location ?? string.Empty).Trim();
        var platform = (query.Platform ?? string.Empty).Trim();
        var vendor = (query.Vendor ?? string.Empty).Trim();

        var filtered = _context.Devices.AsNoTracking().ToList()
            .Where(device =>
            {
                if (query.ManagedOnly && !device.IsManaged)
                {
                    return false;
                }

                if (status.Length > 0 && !string.Equals(device.Status, status, System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (location.Length > 0 && !string.Equals(device.Location, location, System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (platform.Length > 0 && !string.Equals(device.Platform, platform, System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (vendor.Length > 0 && !string.Equals(device.Vendor, vendor, System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (search.Length == 0)
                {
                    return true;
                }

                return string.Join(' ', device.Id, device.Hostname, device.IpAddress, device.Vendor, device.Location, device.Platform)
                    .ToLowerInvariant()
                    .Contains(search);
            })
            .ToList();

        var sorted = Sort(filtered, query.SortBy, query.Descending);

        var pageSize = query.PageSize <= 0 ? 50 : query.PageSize;
        if (pageSize > 500)
        {
            pageSize = 500;
        }

        var page = query.Page <= 0 ? 1 : query.Page;
        var total = sorted.Count;

        return new PagedResult<DeviceRecord>
        {
            Items = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            PageCount = pageSize == 0 ? 0 : (int)System.Math.Ceiling(total / (double)pageSize)
        };
    }

    private static List<DeviceRecord> Sort(List<DeviceRecord> devices, string sortBy, bool descending)
    {
        var key = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
        var direction = descending ? -1 : 1;

        int CompareText(string left, string right)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);
        }

        int ComparePrimary(DeviceRecord left, DeviceRecord right)
        {
            return key switch
            {
                "id" => CompareText(left.Id, right.Id),
                "status" => CompareText(left.Status, right.Status),
                "location" => CompareText(left.Location, right.Location),
                "vendor" => CompareText(left.Vendor, right.Vendor),
                "platform" => CompareText(left.Platform, right.Platform),
                "ipaddress" => CompareText(left.IpAddress, right.IpAddress),
                "lastseen" => CompareText(left.LastSeen, right.LastSeen),
                "cpu" => left.Cpu.CompareTo(right.Cpu),
                "memory" => left.Memory.CompareTo(right.Memory),
                _ => CompareText(left.Hostname, right.Hostname)
            };
        }

        // The index keeps equal keys in their original order, so paging stays stable between calls.
        return devices
            .Select((device, index) => (Device: device, Index: index))
            .OrderBy(entry => entry, Comparer<(DeviceRecord Device, int Index)>.Create((left, right) =>
            {
                var comparison = direction * ComparePrimary(left.Device, right.Device);
                return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
            }))
            .Select(entry => entry.Device)
            .ToList();
    }

    /// <summary>
    /// Removes a device from the fleet together with its metrics and policy assignments.
    /// Alerts already raised against the device are kept for the incident history.
    /// </summary>
    public bool RemoveDevice(string id)
    {
        var device = GetDeviceById(id);
        if (device is null)
        {
            return false;
        }

        var metrics = _context.DeviceMetrics.Where(sample => sample.DeviceId == device.Id).ToList();
        var assignments = _context.PolicyAssignments.Where(assignment => assignment.DeviceId == device.Id).ToList();

        _context.DeviceMetrics.RemoveRange(metrics);
        _context.PolicyAssignments.RemoveRange(assignments);
        _context.Devices.Remove(device);
        _context.SaveChanges();

        foreach (var policyId in assignments.Select(assignment => assignment.PolicyId).Distinct().ToList())
        {
            var policy = GetPolicyById(policyId);
            if (policy is null)
            {
                continue;
            }

            policy.AssignedDevices = _context.PolicyAssignments.Count(assignment => assignment.PolicyId == policy.Id);
        }

        _context.SaveChanges();
        return true;
    }

    /// <summary>
    /// Records a heartbeat against a device and appends the metric sample. Returns null when the
    /// device is unknown.
    /// </summary>
    public (DeviceMetricSample? Sample, DeviceHeartbeatResponse? Response) RecordHeartbeat(
        string deviceId,
        DeviceHeartbeatRequest request,
        MetricsService metrics)
    {
        var device = GetDeviceById(deviceId);
        if (device is null)
        {
            return (null, null);
        }

        var now = DateTimeOffset.UtcNow;
        var sample = metrics.RecordSample(device, request.Cpu, request.Memory, request.Disk, now);
        _context.SaveChanges();

        return (sample, new DeviceHeartbeatResponse
        {
            DeviceId = device.Id,
            Status = device.Status,
            LastSeen = device.LastSeen,
            SampleCount = _context.DeviceMetrics.Count(metric => metric.DeviceId == device.Id)
        });
    }
}
