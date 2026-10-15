using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class MetricsService
{
    public const int DefaultWindowMinutes = 60;
    public const int MaxWindowMinutes = 7 * 24 * 60;
    public const int DefaultMaxSamples = 120;
    public const int MaxSamples = 1000;
    public const int OfflineAfterMinutes = 15;

    private readonly MdmDbContext _context;

    public MetricsService(MdmDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Records a metric sample and refreshes the cached values on the device row so the
    /// dashboard and the rule evaluator both see the latest reading without a join.
    /// </summary>
    public DeviceMetricSample RecordSample(DeviceRecord device, int cpu, int memory, int disk, DateTimeOffset capturedAt)
    {
        var sample = new DeviceMetricSample
        {
            Id = $"MET-{Guid.NewGuid():N}".ToUpperInvariant(),
            DeviceId = device.Id,
            Cpu = Clamp(cpu),
            Memory = Clamp(memory),
            Disk = Clamp(disk),
            CapturedAt = FormatTimestamp(capturedAt),
            CapturedAtEpochMs = capturedAt.ToUnixTimeMilliseconds()
        };

        device.Cpu = sample.Cpu;
        device.Memory = sample.Memory;
        device.Status = "Online";
        device.LastSeen = sample.CapturedAt;

        _context.DeviceMetrics.Add(sample);
        return sample;
    }

    public IReadOnlyList<DeviceMetricSample> GetSamples(string deviceId, int windowMinutes, int maxSamples)
    {
        var window = ClampWindow(windowMinutes);
        var limit = ClampMaxSamples(maxSamples);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-window).ToUnixTimeMilliseconds();

        var samples = _context.DeviceMetrics.AsNoTracking()
            .Where(sample => sample.DeviceId == deviceId && sample.CapturedAtEpochMs >= cutoff)
            .OrderByDescending(sample => sample.CapturedAtEpochMs)
            .Take(limit)
            .ToList();

        samples.Reverse();
        return samples;
    }

    public DeviceMetricWindow GetWindow(string deviceId, int windowMinutes)
    {
        var window = ClampWindow(windowMinutes);
        var samples = GetSamples(deviceId, window, MaxSamples);

        return new DeviceMetricWindow
        {
            DeviceId = deviceId,
            WindowMinutes = window,
            SampleCount = samples.Count,
            AvgCpu = samples.Count == 0 ? 0 : Math.Round(samples.Average(s => s.Cpu), 1),
            AvgMemory = samples.Count == 0 ? 0 : Math.Round(samples.Average(s => s.Memory), 1),
            PeakCpu = samples.Count == 0 ? 0 : samples.Max(s => s.Cpu),
            PeakMemory = samples.Count == 0 ? 0 : samples.Max(s => s.Memory),
            Samples = samples.ToList()
        };
    }

    /// <summary>
    /// Returns every device that has gone quiet for longer than the offline threshold, so the
    /// evaluator can raise an alert without re-reading all devices on each pass.
    /// </summary>
    public IReadOnlyList<DeviceRecord> GetDevicesOfflineFor(DateTimeOffset now, int minutes)
    {
        var threshold = minutes <= 0 ? 1 : minutes;
        var cutoff = now.AddMinutes(-threshold);

        // LastSeen predates this service and mixes timestamp layouts, so it is parsed rather than
        // compared as text. A device with a missing or unreadable stamp is treated as offline.
        return _context.Devices.AsNoTracking().ToList()
            .Where(device =>
            {
                var lastSeen = TryParseTimestamp(device.LastSeen);
                return lastSeen is null || lastSeen.Value < cutoff;
            })
            .ToList();
    }

    /// <summary>
    /// Drops samples older than the retention window. Returns the number of rows removed.
    /// </summary>
    public int Prune(int retentionMinutes)
    {
        var window = ClampWindow(retentionMinutes);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-window).ToUnixTimeMilliseconds();

        var stale = _context.DeviceMetrics
            .Where(sample => sample.CapturedAtEpochMs < cutoff)
            .ToList();

        if (stale.Count == 0)
        {
            return 0;
        }

        _context.DeviceMetrics.RemoveRange(stale);
        _context.SaveChanges();
        return stale.Count;
    }

    public static int Clamp(int percentage)
    {
        if (percentage < 0)
        {
            return 0;
        }

        return percentage > 100 ? 100 : percentage;
    }

    public static int ClampWindow(int windowMinutes)
    {
        if (windowMinutes <= 0)
        {
            return DefaultWindowMinutes;
        }

        return windowMinutes > MaxWindowMinutes ? MaxWindowMinutes : windowMinutes;
    }

    public static int ClampMaxSamples(int maxSamples)
    {
        if (maxSamples <= 0)
        {
            return DefaultMaxSamples;
        }

        return maxSamples > MaxSamples ? MaxSamples : maxSamples;
    }

    /// <summary>
    /// Round-trippable ISO-8601 in UTC. The existing seed data mixes "O" and ISO-8601 forms, so
    /// comparisons use a fixed round-trip layout to stay lexicographically ordered.
    /// </summary>
    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset? TryParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }
    /// <summary>Marks managed devices Offline after the heartbeat timeout and restores Online on a fresh heartbeat.</summary>
    public int RefreshOfflineStatuses(DateTimeOffset now, int minutes)
    {
        var cutoff = now.AddMinutes(-(minutes <= 0 ? 1 : minutes));
        var changed = 0;
        foreach (var device in _context.Devices.Where(d => d.IsManaged).ToList())
        {
            var lastSeen = TryParseTimestamp(device.LastSeen);
            var shouldBeOffline = lastSeen is null || lastSeen.Value < cutoff;
            if (shouldBeOffline && !string.Equals(device.Status, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                device.Status = "Offline";
                changed++;
            }
            else if (!shouldBeOffline && string.Equals(device.Status, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                device.Status = "Online";
                changed++;
            }
        }

        if (changed > 0)
        {
            _context.SaveChanges();
        }

        return changed;
    }

}
