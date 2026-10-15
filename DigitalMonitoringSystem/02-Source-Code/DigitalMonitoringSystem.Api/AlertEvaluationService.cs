using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

/// <summary>
/// Runs the alert rules on a timer so a device that crosses a threshold raises an alert even when
/// nobody is looking at the dashboard. Any change to the fleet pushes a fresh dashboard to the
/// connected clients, the same way the interactive endpoints do.
/// </summary>
public sealed class AlertEvaluationService : BackgroundService
{
    public const string IntervalSetting = "Alerting:EvaluationIntervalSeconds";
    public const string EnabledSetting = "Alerting:Enabled";
    public const string RetentionSetting = "Alerting:MetricRetentionMinutes";

    public const int DefaultIntervalSeconds = 60;
    public const int MinimumIntervalSeconds = 10;
    public const int DefaultRetentionMinutes = 7 * 24 * 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlertEvaluationService> _logger;

    public AlertEvaluationService(
        IServiceScopeFactory scopeFactory,
        IHubContext<DashboardHub> hubContext,
        IConfiguration configuration,
        ILogger<AlertEvaluationService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue(EnabledSetting, true))
        {
            _logger.LogInformation("Background alert evaluation is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(ClampInterval(_configuration.GetValue(IntervalSetting, DefaultIntervalSeconds)));
        _logger.LogInformation("Alert evaluation started on a {Interval} interval.", interval);

        using var timer = new PeriodicTimer(interval);

        // Give the host a moment to finish starting before the first pass.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alert evaluation pass failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One evaluation and metric-pruning pass. Exposed so tests and the manual evaluate endpoint
    /// share exactly the same behaviour.
    /// </summary>
    public async Task<AlertRuleEvaluationResult> EvaluateOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        var rules = provider.GetRequiredService<AlertRuleService>();
        var metrics = provider.GetRequiredService<MetricsService>();
        var dashboard = provider.GetRequiredService<DashboardService>();

        var now = DateTimeOffset.UtcNow;
        var statusChanged = metrics.RefreshOfflineStatuses(now, MetricsService.OfflineAfterMinutes);
        var result = rules.Evaluate(now);

        if (statusChanged > 0 || result.AlertsCreated > 0 || result.AlertsResolved > 0)
        {
            _logger.LogInformation(
                "Alert evaluation created {Created} and resolved {Resolved} alerts across {Rules} rules.",
                result.AlertsCreated,
                result.AlertsResolved,
                result.RulesEvaluated);

            await _hubContext.Clients.All.SendAsync("ReceiveDashboard", dashboard.BuildDashboard(), cancellationToken);
        }

        var retention = _configuration.GetValue(RetentionSetting, DefaultRetentionMinutes);
        var pruned = metrics.Prune(retention);
        if (pruned > 0)
        {
            _logger.LogInformation("Pruned {Count} metric samples older than the retention window.", pruned);
        }

        return result;
    }

    public static int ClampInterval(int seconds)
    {
        if (seconds <= 0)
        {
            return DefaultIntervalSeconds;
        }

        return seconds < MinimumIntervalSeconds ? MinimumIntervalSeconds : seconds;
    }
}
