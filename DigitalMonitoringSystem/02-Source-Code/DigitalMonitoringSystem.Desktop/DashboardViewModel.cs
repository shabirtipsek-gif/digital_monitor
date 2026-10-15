using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;

namespace DigitalMonitoringSystem.Desktop;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    public ObservableCollection<DeviceRow> Devices { get; } = new();
    public ObservableCollection<AlertRow> Alerts { get; } = new();
    public ObservableCollection<PolicyRow> Policies { get; } = new();

    private string _title = "A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT";
    private string _healthStatus = "Loading";
    private int _totalDevices;
    private int _onlineDevices;
    private int _openAlerts;
    private int _criticalAlerts;
    private string? _accessToken;

    public string? AccessToken => _accessToken;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string HealthStatus
    {
        get => _healthStatus;
        private set => SetProperty(ref _healthStatus, value);
    }

    public int TotalDevices
    {
        get => _totalDevices;
        private set => SetProperty(ref _totalDevices, value);
    }

    public int OnlineDevices
    {
        get => _onlineDevices;
        private set => SetProperty(ref _onlineDevices, value);
    }

    public int OpenAlerts
    {
        get => _openAlerts;
        private set => SetProperty(ref _openAlerts, value);
    }

    public int CriticalAlerts
    {
        get => _criticalAlerts;
        private set => SetProperty(ref _criticalAlerts, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5103") };
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        _accessToken = result?.Token;
        return !string.IsNullOrWhiteSpace(_accessToken);
    }

    public async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            HealthStatus = "Signed out";
            return;
        }

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5103") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var summary = await client.GetFromJsonAsync<DashboardSummary>("/api/dashboard") ?? new DashboardSummary();
            var health = await client.GetFromJsonAsync<SystemHealthSnapshot>("/api/system/overview") ?? new SystemHealthSnapshot();
            var policies = await client.GetFromJsonAsync<PolicyDefinition[]>("/api/policies") ?? Array.Empty<PolicyDefinition>();

            ApplySummary(summary, policies, health.Status);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to load dashboard data: {ex.Message}", "MDM Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            HealthStatus = "Offline";
        }
    }

    public void UpdateFromSummary(DashboardSummary summary)
    {
        ApplySummary(summary, Array.Empty<PolicyDefinition>(), HealthStatus);
    }

    private void ApplySummary(DashboardSummary summary, PolicyDefinition[] policies, string healthStatus)
    {
        Devices.Clear();
        Alerts.Clear();
        Policies.Clear();

        foreach (var device in summary.Devices)
        {
            Devices.Add(new DeviceRow(device.Hostname, device.IpAddress, device.Status, device.Cpu, device.Memory));
        }

        foreach (var alert in summary.Alerts)
        {
            Alerts.Add(new AlertRow(alert.Title, alert.Severity, alert.Source));
        }

        foreach (var policy in policies)
        {
            Policies.Add(new PolicyRow(policy.Name, policy.Category, policy.Status, policy.AssignedDevices));
        }

        TotalDevices = summary.TotalDevices;
        OnlineDevices = summary.OnlineDevices;
        OpenAlerts = summary.OpenAlerts;
        CriticalAlerts = summary.CriticalAlerts;
        Title = "A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT";
        HealthStatus = healthStatus;
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AuthenticationResponse
{
    public string Token { get; set; } = string.Empty;
}

public sealed class DeviceRow
{
    public string Name { get; }
    public string IpAddress { get; }
    public string Status { get; }
    public int Cpu { get; }
    public int Memory { get; }

    public DeviceRow(string name, string ipAddress, string status, int cpu, int memory)
    {
        Name = name;
        IpAddress = ipAddress;
        Status = status;
        Cpu = cpu;
        Memory = memory;
    }
}

public sealed class AlertRow
{
    public string Title { get; }
    public string Severity { get; }
    public string Source { get; }

    public AlertRow(string title, string severity, string source)
    {
        Title = title;
        Severity = severity;
        Source = source;
    }
}

public sealed class PolicyRow
{
    public string Name { get; }
    public string Category { get; }
    public string Status { get; }
    public int AssignedDevices { get; }

    public PolicyRow(string name, string category, string status, int assignedDevices)
    {
        Name = name;
        Category = category;
        Status = status;
        AssignedDevices = assignedDevices;
    }
}

public sealed class DashboardSummary
{
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int OpenAlerts { get; set; }
    public int CriticalAlerts { get; set; }
    public double AvgCpu { get; set; }
    public double AvgMemory { get; set; }
    public DeviceSummaryItem[] Devices { get; set; } = Array.Empty<DeviceSummaryItem>();
    public AlertSummaryItem[] Alerts { get; set; } = Array.Empty<AlertSummaryItem>();
}

public sealed class DeviceSummaryItem
{
    public string Id { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Cpu { get; set; }
    public int Memory { get; set; }
    public string Location { get; set; } = string.Empty;
}

public sealed class AlertSummaryItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public bool Acknowledged { get; set; }
}

public sealed class SystemHealthSnapshot
{
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset LastSyncUtc { get; set; }
}

public sealed class PolicyDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AssignedDevices { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}
