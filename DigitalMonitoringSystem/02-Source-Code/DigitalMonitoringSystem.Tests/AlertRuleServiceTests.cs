using DigitalMonitoringSystem.Api;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Tests;

public class AlertRuleServiceTests
{
    private static AlertRuleRequest CpuRule(int threshold = 90, int durationMinutes = 0, string severity = "High") => new()
    {
        Name = "Sustained CPU pressure",
        Metric = AlertRuleService.MetricCpu,
        Comparison = AlertRuleService.ComparisonGreaterThan,
        Threshold = threshold,
        DurationMinutes = durationMinutes,
        Severity = severity,
        Enabled = true
    };

    [Fact]
    public void FirstUse_SeedsTheExampleRules()
    {
        using var context = TestData.CreateInMemory();
        new AlertRuleService(context, new MetricsService(context));

        var rules = new AlertRuleService(context, new MetricsService(context)).GetRules();

        Assert.Equal(4, rules.Count);
        Assert.Contains(rules, rule => rule.Id == "RULE-CPU-90");
        Assert.Contains(rules, rule => rule.Metric == AlertRuleService.MetricOffline);
    }

    [Fact]
    public void Create_ReturnsTheStoredRule()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));

        var (rule, errors) = service.Create(CpuRule(threshold: 75, durationMinutes: 3, severity: "Medium"));

        Assert.Empty(errors);
        Assert.NotNull(rule);
        Assert.StartsWith("RULE-", rule!.Id);
        Assert.Equal("Sustained CPU pressure", rule.Name);
        Assert.Equal(75, rule.Threshold);
        Assert.Equal(3, rule.DurationMinutes);
        Assert.Equal("Medium", rule.Severity);
        Assert.True(rule.Enabled);
        Assert.NotNull(service.GetRuleById(rule.Id));
    }

    [Theory]
    [InlineData("", AlertRuleService.MetricCpu, 90, 0, "High", "name")]
    [InlineData("Rule", "Temperature", 90, 0, "High", "metric")]
    [InlineData("Rule", AlertRuleService.MetricCpu, 90, 0, "Catastrophic", "severity")]
    [InlineData("Rule", AlertRuleService.MetricCpu, 120, 0, "High", "threshold")]
    [InlineData("Rule", AlertRuleService.MetricCpu, -5, 0, "High", "threshold")]
    [InlineData("Rule", AlertRuleService.MetricCpu, 90, -1, "High", "durationMinutes")]
    [InlineData("Rule", AlertRuleService.MetricOffline, 0, 0, "High", "threshold")]
    [InlineData("Rule", AlertRuleService.MetricOffline, 15, 0, "High", "comparison")]
    public void Create_RejectsInvalidRules(string name, string metric, int threshold, int duration, string severity, string expectedField)
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));

        var (rule, errors) = service.Create(new AlertRuleRequest
        {
            Name = name,
            Metric = metric,
            Comparison = metric == AlertRuleService.MetricOffline
                ? AlertRuleService.ComparisonLessThan
                : AlertRuleService.ComparisonGreaterThan,
            Threshold = threshold,
            DurationMinutes = duration,
            Severity = severity,
            Enabled = true
        });

        Assert.Null(rule);
        Assert.Contains(errors, error => error.Field == expectedField);
    }

    [Fact]
    public void Create_RejectsAnUnknownComparison()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));

        var request = CpuRule();
        request.Comparison = "Approximately";

        var (rule, errors) = service.Create(request);

        Assert.Null(rule);
        Assert.Contains(errors, error => error.Field == "comparison");
    }

    [Fact]
    public void Update_ChangesAnExistingRule()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));
        var (created, _) = service.Create(CpuRule());

        var (updated, errors) = service.Update(created!.Id.ToLowerInvariant(), new AlertRuleRequest
        {
            Name = "Relaxed CPU pressure",
            Metric = AlertRuleService.MetricMemory,
            Comparison = AlertRuleService.ComparisonLessThan,
            Threshold = 20,
            DurationMinutes = 7,
            Severity = "Low",
            Enabled = false
        });

        Assert.Empty(errors);
        Assert.NotNull(updated);
        Assert.Equal("Relaxed CPU pressure", updated!.Name);
        Assert.Equal(AlertRuleService.MetricMemory, updated.Metric);
        Assert.False(updated.Enabled);
        Assert.Null(service.GetRuleById(created.Id.ToUpperInvariant() + "-missing"));
    }

    [Fact]
    public void Update_ReportsAnUnknownRuleOnTheIdField()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));

        var (rule, errors) = service.Update("RULE-nope", CpuRule());

        Assert.Null(rule);
        Assert.Contains(errors, error => error.Field == "id");
    }

    [Fact]
    public void Update_LeavesTheStoredRuleUntouchedWhenValidationFails()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));
        var (created, _) = service.Create(CpuRule(threshold: 80));

        var request = CpuRule(threshold: 999);
        var (rule, errors) = service.Update(created!.Id, request);

        Assert.Null(rule);
        Assert.NotEmpty(errors);
        Assert.Equal(80, service.GetRuleById(created.Id)!.Threshold);
    }

    [Fact]
    public void Delete_RemovesTheRuleAndClosesItsOpenAlert()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        var (rule, _) = service.Create(CpuRule(threshold: 10));

        var device = TestData.Device(cpu: 95);
        context.Devices.Add(device);
        metrics.RecordSample(device, 95, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        Assert.Equal(1, service.Evaluate(DateTimeOffset.UtcNow).AlertsCreated);

        Assert.True(service.Delete(rule!.Id));

        // The alert is closed rather than deleted: it stays as incident history.
        var alert = Assert.Single(context.Alerts.ToList());
        Assert.True(alert.Acknowledged);
        Assert.Null(service.GetRuleById(rule.Id));
    }

    [Fact]
    public void Delete_ReturnsFalseForAnUnknownRule()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));

        Assert.False(service.Delete("RULE-missing"));
    }

    [Fact]
    public void Evaluate_RaisesAnAlertWhenAThresholdIsCrossed()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90, severity: "High"));

        var device = TestData.Device(cpu: 95);
        context.Devices.Add(device);
        metrics.RecordSample(device, 95, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var result = service.Evaluate(DateTimeOffset.UtcNow);

        Assert.Equal(1, result.RulesEvaluated);
        Assert.Equal(1, result.AlertsCreated);
        var created = Assert.Single(result.CreatedAlerts);
        Assert.Equal(device.Hostname, created.Source);
        Assert.Equal("High", created.Severity);
        Assert.Contains("90%", created.Title);

        var stored = Assert.Single(context.Alerts.ToList());
        Assert.Equal(device.Hostname, stored.Source);
        Assert.False(stored.Acknowledged);
        Assert.StartsWith("RULE-", stored.RuleId);
    }

    [Fact]
    public void Evaluate_StaysQuietWhileTheReadingIsBelowTheThreshold()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90));

        var device = TestData.Device(cpu: 20);
        context.Devices.Add(device);
        metrics.RecordSample(device, 20, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var result = service.Evaluate(DateTimeOffset.UtcNow);

        Assert.Equal(0, result.AlertsCreated);
        Assert.Empty(context.Alerts.ToList());
    }

    [Fact]
    public void Evaluate_IgnoresDevicesThatHaveNeverReported()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 5));

        context.Devices.Add(TestData.Device(cpu: 99));
        context.SaveChanges();

        Assert.Equal(0, service.Evaluate(DateTimeOffset.UtcNow).AlertsCreated);
    }

    [Fact]
    public void Evaluate_WaitsForTheBreachToLastTheWholeDuration()
    {
        using var context = TestData.CreateInMemory();
        var now = DateTimeOffset.UtcNow;
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90, durationMinutes: 10));

        var device = TestData.Device();
        context.Devices.Add(device);
        // A single high reading is not a ten minute breach.
        context.DeviceMetrics.Add(TestData.Sample(device.Id, now.AddMinutes(-2), cpu: 95));
        context.SaveChanges();

        Assert.Equal(0, service.Evaluate(now).AlertsCreated);

        context.DeviceMetrics.Add(TestData.Sample(device.Id, now.AddMinutes(-11), cpu: 95));
        context.SaveChanges();

        Assert.Equal(1, service.Evaluate(now).AlertsCreated);
    }

    [Fact]
    public void Evaluate_StaysQuietWhenAnOldReadingIsHealthy()
    {
        using var context = TestData.CreateInMemory();
        var now = DateTimeOffset.UtcNow;
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90, durationMinutes: 10));

        var device = TestData.Device();
        context.Devices.Add(device);
        context.DeviceMetrics.AddRange(
            TestData.Sample(device.Id, now.AddMinutes(-11), cpu: 95),
            TestData.Sample(device.Id, now.AddMinutes(-5), cpu: 10));
        context.SaveChanges();

        // The window is breached by the oldest sample but not the newest one.
        Assert.Equal(0, service.Evaluate(now).AlertsCreated);
    }

    [Fact]
    public void Evaluate_DoesNotRaiseTheSameAlertTwice()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90));

        var device = TestData.Device(cpu: 95);
        context.Devices.Add(device);
        metrics.RecordSample(device, 95, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        Assert.Equal(1, service.Evaluate(DateTimeOffset.UtcNow).AlertsCreated);

        var second = service.Evaluate(DateTimeOffset.UtcNow);

        Assert.Equal(0, second.AlertsCreated);
        Assert.Equal(1, second.AlertsSkipped);
        Assert.Single(context.Alerts.ToList());
    }

    [Fact]
    public void Evaluate_KeepsAnAcknowledgedAlertFromBlockingANewOne()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90));

        var device = TestData.Device(cpu: 95);
        context.Devices.Add(device);
        metrics.RecordSample(device, 95, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var first = Assert.Single(service.Evaluate(DateTimeOffset.UtcNow).CreatedAlertIds);
        var dashboard = new DashboardService(context);
        Assert.True(dashboard.AcknowledgeAlert(first));

        Assert.Equal(1, service.Evaluate(DateTimeOffset.UtcNow).AlertsCreated);
    }

    [Fact]
    public void Evaluate_ClosesTheAlertOnceTheDeviceRecovers()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90));

        var device = TestData.Device(cpu: 95);
        context.Devices.Add(device);
        metrics.RecordSample(device, 95, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var alertId = Assert.Single(service.Evaluate(DateTimeOffset.UtcNow).CreatedAlertIds);

        metrics.RecordSample(device, 12, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var recovery = service.Evaluate(DateTimeOffset.UtcNow);

        Assert.Equal(1, recovery.AlertsResolved);
        Assert.Equal(0, recovery.AlertsCreated);
        Assert.True(context.Alerts.Single(a => a.Id == alertId).Acknowledged);
    }

    [Fact]
    public void EvaluateForDevice_DoesNotResolveAnotherDevicesOpenAlert()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90));

        var first = TestData.Device(id: "DEV-FIRST", hostname: "first-01", cpu: 95);
        var second = TestData.Device(id: "DEV-SECOND", hostname: "second-01", cpu: 95);
        var initialSampleTime = DateTimeOffset.UtcNow;
        context.Devices.AddRange(first, second);
        metrics.RecordSample(first, 95, 20, 20, initialSampleTime);
        metrics.RecordSample(second, 95, 20, 20, initialSampleTime);
        context.SaveChanges();

        var created = service.Evaluate(initialSampleTime);
        Assert.Equal(2, created.AlertsCreated);

        var recoverySampleTime = initialSampleTime.AddSeconds(1);
        metrics.RecordSample(second, 10, 20, 20, recoverySampleTime);
        context.SaveChanges();

        var heartbeatEvaluation = service.Evaluate(recoverySampleTime, first.Id);

        Assert.Equal(0, heartbeatEvaluation.AlertsResolved);
        Assert.All(context.Alerts.ToList(), alert => Assert.False(alert.Acknowledged));

        var fleetEvaluation = service.Evaluate(recoverySampleTime);
        Assert.Equal(1, fleetEvaluation.AlertsResolved);
        Assert.True(context.Alerts.Single(alert => alert.Source == second.Hostname).Acknowledged);
    }

    [Fact]
    public void Evaluate_LeavesManuallyAcknowledgedAlertsAlone()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90));

        var device = TestData.Device(cpu: 95);
        context.Devices.Add(device);
        metrics.RecordSample(device, 95, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var alertId = Assert.Single(service.Evaluate(DateTimeOffset.UtcNow).CreatedAlertIds);
        new DashboardService(context).AcknowledgeAlert(alertId);

        var result = service.Evaluate(DateTimeOffset.UtcNow);

        // Already acknowledged alerts are not counted as resolved by the rule engine.
        Assert.Equal(0, result.AlertsResolved);
    }

    [Fact]
    public void Evaluate_HandlesEachDeviceSeparately()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(CpuRule(threshold: 90));

        var hot = TestData.Device(id: "DEV-HOT", hostname: "hot-01");
        var cool = TestData.Device(id: "DEV-COOL", hostname: "cool-01");
        context.Devices.AddRange(hot, cool);
        metrics.RecordSample(hot, 95, 20, 20, DateTimeOffset.UtcNow);
        metrics.RecordSample(cool, 15, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var result = service.Evaluate(DateTimeOffset.UtcNow);

        Assert.Equal(1, result.AlertsCreated);
        Assert.Equal("hot-01", Assert.Single(context.Alerts.ToList()).Source);
    }

    [Fact]
    public void Evaluate_SkipsDisabledRules()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);

        var request = CpuRule(threshold: 90);
        request.Enabled = false;
        service.Create(request);

        var device = TestData.Device(cpu: 99);
        context.Devices.Add(device);
        metrics.RecordSample(device, 99, 20, 20, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var result = service.Evaluate(DateTimeOffset.UtcNow);

        Assert.Equal(0, result.RulesEvaluated);
        Assert.Empty(context.Alerts.ToList());
    }

    [Fact]
    public void Evaluate_RaisesAnAlertForAnOfflineDevice()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));
        TestData.ClearSeededRules(context);
        service.Create(new AlertRuleRequest
        {
            Name = "Device heartbeat missed",
            Metric = AlertRuleService.MetricOffline,
            Comparison = AlertRuleService.ComparisonGreaterThan,
            Threshold = 15,
            DurationMinutes = 0,
            Severity = "Medium",
            Enabled = true
        });

        context.Devices.AddRange(
            TestData.Device(id: "DEV-ONLINE", hostname: "online-01", lastSeen: DateTimeOffset.UtcNow.AddMinutes(-2)),
            TestData.Device(id: "DEV-GONE", hostname: "gone-01", lastSeen: DateTimeOffset.UtcNow.AddMinutes(-40)));
        context.SaveChanges();

        var result = service.Evaluate(DateTimeOffset.UtcNow);

        var created = Assert.Single(result.CreatedAlerts);
        Assert.Equal("gone-01", created.Source);
        Assert.Contains("offline", created.Title);
    }

    [Fact]
    public void Evaluate_SupportsLessThanRules()
    {
        using var context = TestData.CreateInMemory();
        var metrics = new MetricsService(context);
        var service = new AlertRuleService(context, metrics);
        TestData.ClearSeededRules(context);
        service.Create(new AlertRuleRequest
        {
            Name = "Disk running low",
            Metric = AlertRuleService.MetricDisk,
            Comparison = AlertRuleService.ComparisonLessThan,
            Threshold = 10,
            DurationMinutes = 0,
            Severity = "Low",
            Enabled = true
        });

        var starving = TestData.Device(id: "DEV-STARVING", hostname: "starving-01");
        var healthy = TestData.Device(id: "DEV-HEALTHY", hostname: "healthy-01");
        context.Devices.AddRange(starving, healthy);
        metrics.RecordSample(starving, 50, 50, 4, DateTimeOffset.UtcNow);
        metrics.RecordSample(healthy, 50, 50, 80, DateTimeOffset.UtcNow);
        context.SaveChanges();

        var created = Assert.Single(service.Evaluate(DateTimeOffset.UtcNow).CreatedAlerts);

        Assert.Equal("starving-01", created.Source);
        Assert.Contains("Disk", created.Title);
    }

    [Fact]
    public void Evaluate_ReturnsAnEmptyResultWhenThereAreNoRules()
    {
        using var context = TestData.CreateInMemory();
        var service = new AlertRuleService(context, new MetricsService(context));
        TestData.ClearSeededRules(context);
        context.Devices.Add(TestData.Device(cpu: 99));
        context.SaveChanges();

        var result = service.Evaluate(DateTimeOffset.UtcNow);

        Assert.Equal(0, result.RulesEvaluated);
        Assert.Equal(0, result.AlertsCreated);
        Assert.NotEqual(default, MetricsService.TryParseTimestamp(result.EvaluatedAtUtc));
    }
}
