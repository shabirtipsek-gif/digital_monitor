using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class AlertRuleService
{
    public const string MetricCpu = "Cpu";
    public const string MetricMemory = "Memory";
    public const string MetricDisk = "Disk";
    public const string MetricOffline = "Offline";

    public const string ComparisonGreaterThan = "GreaterThan";
    public const string ComparisonLessThan = "LessThan";

    public static readonly string[] SupportedMetrics = { MetricCpu, MetricMemory, MetricDisk, MetricOffline };
    public static readonly string[] SupportedComparisons = { ComparisonGreaterThan, ComparisonLessThan };
    public static readonly string[] SupportedSeverities = { "High", "Medium", "Low", "Info" };

    public const int MaxDurationMinutes = 7 * 24 * 60;
    public const int MaxThresholdMinutes = 30 * 24 * 60;

    /// <summary>Extra headroom on the sample lookup so a full-duration run can be observed.</summary>
    public const int DurationLookbackMarginMinutes = 2;

    private readonly MdmDbContext _context;
    private readonly MetricsService _metrics;

    public AlertRuleService(MdmDbContext context, MetricsService metrics)
    {
        _context = context;
        _metrics = metrics;
        EnsureSeedRules();
    }

    public IReadOnlyList<AlertRule> GetRules() => _context.AlertRules.AsNoTracking().OrderBy(r => r.Id).ToList().AsReadOnly();

    public AlertRule? GetRuleById(string id)
    {
        return Lookup(_context.AlertRules, id);
    }

    public IReadOnlyList<AlertRuleValidationError> Validate(AlertRuleRequest request)
    {
        var errors = new List<AlertRuleValidationError>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new AlertRuleValidationError { Field = "name", Message = "Rule name is required." });
        }

        if (!SupportedMetrics.Contains(request.Metric, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new AlertRuleValidationError
            {
                Field = "metric",
                Message = $"Metric must be one of {string.Join(", ", SupportedMetrics)}."
            });
        }

        if (!SupportedComparisons.Contains(request.Comparison, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new AlertRuleValidationError
            {
                Field = "comparison",
                Message = $"Comparison must be one of {string.Join(", ", SupportedComparisons)}."
            });
        }

        if (!SupportedSeverities.Contains(request.Severity, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new AlertRuleValidationError
            {
                Field = "severity",
                Message = $"Severity must be one of {string.Join(", ", SupportedSeverities)}."
            });
        }

        if (request.DurationMinutes < 0 || request.DurationMinutes > MaxDurationMinutes)
        {
            errors.Add(new AlertRuleValidationError
            {
                Field = "durationMinutes",
                Message = $"Duration must be between 0 and {MaxDurationMinutes} minutes."
            });
        }

        if (string.Equals(request.Metric, MetricOffline, StringComparison.OrdinalIgnoreCase))
        {
            if (request.Threshold < 1 || request.Threshold > MaxThresholdMinutes)
            {
                errors.Add(new AlertRuleValidationError
                {
                    Field = "threshold",
                    Message = $"Offline threshold must be between 1 and {MaxThresholdMinutes} minutes."
                });
            }

            if (string.Equals(request.Comparison, ComparisonLessThan, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new AlertRuleValidationError
                {
                    Field = "comparison",
                    Message = "Offline rules only support the GreaterThan comparison."
                });
            }
        }
        else if (request.Threshold < 0 || request.Threshold > 100)
        {
            errors.Add(new AlertRuleValidationError
            {
                Field = "threshold",
                Message = "Percentage threshold must be between 0 and 100."
            });
        }

        return errors;
    }

    public (AlertRule? Rule, IReadOnlyList<AlertRuleValidationError> Errors) Create(AlertRuleRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var rule = new AlertRule
        {
            Id = $"RULE-{Guid.NewGuid():N}".ToUpperInvariant().Substring(0, 13),
            UpdatedAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow)
        };

        ApplyRequest(rule, request);
        _context.AlertRules.Add(rule);
        _context.SaveChanges();

        return (rule, errors);
    }

    public (AlertRule? Rule, IReadOnlyList<AlertRuleValidationError> Errors) Update(string id, AlertRuleRequest request)
    {
        var rule = GetRuleById(id);
        if (rule is null)
        {
            return (null, new List<AlertRuleValidationError>
            {
                new() { Field = "id", Message = $"Alert rule '{id}' was not found." }
            });
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        ApplyRequest(rule, request);
        _context.SaveChanges();
        return (rule, errors);
    }

    /// <summary>
    /// Deletes a rule and closes any alert it still has open, so the incident queue does not
    /// keep a reference to a rule that no longer exists.
    /// </summary>
    public bool Delete(string id)
    {
        var rule = GetRuleById(id);
        if (rule is null)
        {
            return false;
        }

        var open = _context.Alerts.Where(alert => alert.RuleId == rule.Id && !alert.Acknowledged).ToList();
        foreach (var alert in open)
        {
            alert.Acknowledged = true;
        }

        _context.AlertRules.Remove(rule);
        _context.SaveChanges();
        return true;
    }

    /// <summary>
    /// Runs enabled rules against the current fleet, or one device when a heartbeat arrives.
    /// A breach must hold for the rule's duration before an alert is raised, and a rule that
    /// stops breaching resolves its own open alert.
    /// </summary>
    public AlertRuleEvaluationResult Evaluate(DateTimeOffset now, string? deviceId = null)
    {
        var result = new AlertRuleEvaluationResult
        {
            EvaluatedAtUtc = MetricsService.FormatTimestamp(now)
        };

        var rules = _context.AlertRules.Where(rule => rule.Enabled).ToList();
        result.RulesEvaluated = rules.Count;

        var devices = string.IsNullOrWhiteSpace(deviceId)
            ? _context.Devices.ToList()
            : _context.Devices.Where(device => device.Id == deviceId).ToList();
        var changed = false;

        foreach (var rule in rules)
        {
            var breachingDevices = new List<DeviceRecord>();

            foreach (var device in devices)
            {
                if (IsBreaching(rule, device, now))
                {
                    breachingDevices.Add(device);
                }
            }

            var openAlerts = _context.Alerts
                .Where(alert => alert.RuleId == rule.Id && !alert.Acknowledged);
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var hostname = devices.FirstOrDefault()?.Hostname;
                openAlerts = hostname is null
                    ? openAlerts.Where(_ => false)
                    : openAlerts.Where(alert => alert.Source.ToLower() == hostname.ToLower());
            }

            var alreadyOpen = openAlerts.ToList();

            var openDeviceIds = new HashSet<string>(alreadyOpen.Select(alert => alert.Source), StringComparer.OrdinalIgnoreCase);

            foreach (var device in breachingDevices)
            {
                if (openDeviceIds.Contains(device.Hostname))
                {
                    result.AlertsSkipped++;
                    continue;
                }

                var alert = new AlertRecord
                {
                    Id = $"ALT-{Guid.NewGuid():N}".ToUpperInvariant().Substring(0, 13),
                    Title = BuildTitle(rule, device),
                    Severity = rule.Severity,
                    Source = device.Hostname,
                    Timestamp = MetricsService.FormatTimestamp(now),
                    Acknowledged = false,
                    RuleId = rule.Id
                };

                _context.Alerts.Add(alert);
                result.AlertsCreated++;
                result.CreatedAlertIds.Add(alert.Id);
                result.CreatedAlerts.Add(new AlertSummary
                {
                    Id = alert.Id,
                    Title = alert.Title,
                    Severity = alert.Severity,
                    Source = alert.Source,
                    Timestamp = alert.Timestamp,
                    Acknowledged = alert.Acknowledged,
                    RuleId = alert.RuleId
                });
                changed = true;
            }

            var stillBreachingHostnames = new HashSet<string>(breachingDevices.Select(d => d.Hostname), StringComparer.OrdinalIgnoreCase);
            foreach (var alert in alreadyOpen)
            {
                if (stillBreachingHostnames.Contains(alert.Source))
                {
                    continue;
                }

                alert.Acknowledged = true;
                result.AlertsResolved++;
                changed = true;
            }
        }

        if (changed)
        {
            _context.SaveChanges();
        }

        return result;
    }

    private bool IsBreaching(AlertRule rule, DeviceRecord device, DateTimeOffset now)
    {
        if (string.Equals(rule.Metric, MetricOffline, StringComparison.OrdinalIgnoreCase))
        {
            var lastSeen = MetricsService.TryParseTimestamp(device.LastSeen);
            if (lastSeen is null)
            {
                return true;
            }

            var minutesOffline = (now - lastSeen.Value).TotalMinutes;
            return minutesOffline >= rule.Threshold;
        }

        var duration = Math.Max(rule.DurationMinutes, 0);

        // The lookup reaches slightly further back than the rule duration so the reading that
        // started the run is still in the result, otherwise a breach that is exactly one
        // duration old can never satisfy the duration test.
        var lookback = duration <= 0 ? 1 : duration + DurationLookbackMarginMinutes;
        var samples = _metrics.GetSamples(device.Id, lookback, MetricsService.MaxSamples);

        if (samples.Count == 0)
        {
            // A rule cannot be evaluated until the device has reported at least once.
            return false;
        }

        // The newest reading decides the current state: a device that has recovered is not
        // breaching any more.
        var latest = samples[samples.Count - 1];
        if (!Compare(ReadMetric(latest, rule.Metric), rule))
        {
            return false;
        }

        if (duration <= 0)
        {
            return true;
        }

        // Walk back across the unbroken run of breaching readings and check how long it has held.
        var runStart = latest.CapturedAtEpochMs;
        for (var index = samples.Count - 2; index >= 0; index--)
        {
            var sample = samples[index];
            if (sample.CapturedAtEpochMs == 0 || !Compare(ReadMetric(sample, rule.Metric), rule))
            {
                break;
            }

            runStart = sample.CapturedAtEpochMs;
        }

        return now.ToUnixTimeMilliseconds() - runStart >= (long)duration * 60_000L;
    }

    private static int ReadMetric(DeviceMetricSample sample, string metric)
    {
        if (string.Equals(metric, MetricMemory, StringComparison.OrdinalIgnoreCase))
        {
            return sample.Memory;
        }

        if (string.Equals(metric, MetricDisk, StringComparison.OrdinalIgnoreCase))
        {
            return sample.Disk;
        }

        return sample.Cpu;
    }

    private static bool Compare(int value, AlertRule rule)
    {
        return string.Equals(rule.Comparison, ComparisonLessThan, StringComparison.OrdinalIgnoreCase)
            ? value < rule.Threshold
            : value > rule.Threshold;
    }

    private static string BuildTitle(AlertRule rule, DeviceRecord device)
    {
        if (string.Equals(rule.Metric, MetricOffline, StringComparison.OrdinalIgnoreCase))
        {
            return $"{device.Hostname} offline for more than {rule.Threshold} minutes";
        }

        var label = string.Equals(rule.Metric, MetricMemory, StringComparison.OrdinalIgnoreCase)
            ? "Memory"
            : string.Equals(rule.Metric, MetricDisk, StringComparison.OrdinalIgnoreCase) ? "Disk" : "CPU";

        return $"{device.Hostname} {label} {rule.Comparison.ToLowerInvariant()} {rule.Threshold}% for {rule.DurationMinutes} min";
    }

    private void ApplyRequest(AlertRule rule, AlertRuleRequest request)
    {
        rule.Name = request.Name.Trim();
        rule.Metric = request.Metric.Trim();
        rule.Comparison = request.Comparison.Trim();
        rule.Threshold = request.Threshold;
        rule.DurationMinutes = request.DurationMinutes;
        rule.Severity = request.Severity.Trim();
        rule.Enabled = request.Enabled;
        rule.UpdatedAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow);
    }

    private static TEntity? Lookup<TEntity>(DbSet<TEntity> set, string id) where TEntity : class
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

    private void EnsureSeedRules()
    {
        if (_context.AlertRules.Any())
        {
            return;
        }

        var now = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow);

        _context.AlertRules.AddRange(new List<AlertRule>
        {
            new()
            {
                Id = "RULE-CPU-90", Name = "Sustained CPU pressure", Metric = MetricCpu, Comparison = ComparisonGreaterThan,
                Threshold = 90, DurationMinutes = 10, Severity = "High", Enabled = true, UpdatedAt = now
            },
            new()
            {
                Id = "RULE-MEM-85", Name = "Memory exhaustion risk", Metric = MetricMemory, Comparison = ComparisonGreaterThan,
                Threshold = 85, DurationMinutes = 5, Severity = "High", Enabled = true, UpdatedAt = now
            },
            new()
            {
                Id = "RULE-DSK-90", Name = "Disk almost full", Metric = MetricDisk, Comparison = ComparisonGreaterThan,
                Threshold = 90, DurationMinutes = 0, Severity = "Medium", Enabled = true, UpdatedAt = now
            },
            new()
            {
                Id = "RULE-OFF-15", Name = "Device heartbeat missed", Metric = MetricOffline, Comparison = ComparisonGreaterThan,
                Threshold = MetricsService.OfflineAfterMinutes, DurationMinutes = 0, Severity = "Medium", Enabled = true, UpdatedAt = now
            }
        });

        _context.SaveChanges();
    }
}
