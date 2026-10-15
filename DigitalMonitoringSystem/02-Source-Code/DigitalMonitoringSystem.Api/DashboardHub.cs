using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class DashboardHub : Hub
{
    private readonly DashboardService _dashboardService;

    public DashboardHub(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public override async Task OnConnectedAsync()
    {
        var dashboard = _dashboardService.BuildDashboard();
        await Clients.Caller.SendAsync("ReceiveDashboard", dashboard);
        await base.OnConnectedAsync();
    }

    public DashboardSummary GetDashboard()
    {
        return _dashboardService.BuildDashboard();
    }

    public SystemHealthSnapshot GetSystemHealth()
    {
        return _dashboardService.GetSystemHealth();
    }
}
