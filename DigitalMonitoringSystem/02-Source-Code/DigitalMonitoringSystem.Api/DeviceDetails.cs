using System.Collections.Generic;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class DeviceDetails
{
    public string Id { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Status { get; set; } = "Online";
    public int Cpu { get; set; }
    public int Memory { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Platform { get; set; } = "Windows";
    public string LastSeen { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public List<AlertSummary> OpenAlerts { get; set; } = new List<AlertSummary>();
}
