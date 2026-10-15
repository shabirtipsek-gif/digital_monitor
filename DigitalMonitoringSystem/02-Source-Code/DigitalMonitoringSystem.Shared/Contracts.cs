using System;
using System.Collections.Generic;

namespace DigitalMonitoringSystem.Shared
{
    public sealed class DeviceSummary
    {
        public string Id { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Status { get; set; } = "Online";
        public int Cpu { get; set; }
        public int Memory { get; set; }
        public string Location { get; set; } = string.Empty;
    }

    public sealed class AlertSummary
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info";
        public string Source { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public bool Acknowledged { get; set; }
        public string RuleId { get; set; } = string.Empty;
    }

    public sealed class DashboardSummary
    {
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OpenAlerts { get; set; }
        public int CriticalAlerts { get; set; }
        public double AvgCpu { get; set; }
        public double AvgMemory { get; set; }
        public List<DeviceSummary> Devices { get; set; } = new List<DeviceSummary>();
        public List<AlertSummary> Alerts { get; set; } = new List<AlertSummary>();
        public List<PolicyDefinition> Policies { get; set; } = new List<PolicyDefinition>();
    }

    public sealed class SystemHealthSnapshot
    {
        public string ServiceName { get; set; } = "DigitalMonitoringSystem.Api";
        public string Status { get; set; } = "Healthy";
        public DateTimeOffset LastSyncUtc { get; set; } = DateTimeOffset.UtcNow;
        public DashboardSummary Summary { get; set; } = new DashboardSummary();
    }

    public sealed class DeviceRecord
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
        public string LastSeen { get; set; } = DateTime.UtcNow.ToString("o");
        public bool IsManaged { get; set; } = true;
    }

    public sealed class AlertRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info";
        public string Source { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public bool Acknowledged { get; set; }
        public string RuleId { get; set; } = string.Empty;
    }

    public sealed class PolicyDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public int AssignedDevices { get; set; }
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public sealed class DeviceMetricSample
    {
        public string Id { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public int Cpu { get; set; }
        public int Memory { get; set; }
        public int Disk { get; set; }
        public string CapturedAt { get; set; } = string.Empty;

        /// <summary>
        /// Unix milliseconds for CapturedAt. Range queries use this because comparing formatted
        /// timestamps as text is not something the database can translate into an index seek.
        /// </summary>
        public long CapturedAtEpochMs { get; set; }
    }

    public sealed class DeviceHeartbeatRequest
    {
        public int Cpu { get; set; }
        public int Memory { get; set; }
        public int Disk { get; set; }
        public string AgentVersion { get; set; } = string.Empty;
    }

    public sealed class DeviceHeartbeatResponse
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string LastSeen { get; set; } = string.Empty;
        public int SampleCount { get; set; }
        public List<string> TriggeredAlertIds { get; set; } = new List<string>();
    }

    public sealed class DeviceMetricWindow
    {
        public string DeviceId { get; set; } = string.Empty;
        public int WindowMinutes { get; set; }
        public int SampleCount { get; set; }
        public double AvgCpu { get; set; }
        public double AvgMemory { get; set; }
        public int PeakCpu { get; set; }
        public int PeakMemory { get; set; }
        public List<DeviceMetricSample> Samples { get; set; } = new List<DeviceMetricSample>();
    }

    public sealed class AlertRule
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Metric { get; set; } = "Cpu";
        public string Comparison { get; set; } = "GreaterThan";
        public int Threshold { get; set; }
        public int DurationMinutes { get; set; }
        public string Severity { get; set; } = "Medium";
        public bool Enabled { get; set; } = true;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public sealed class AlertRuleRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Metric { get; set; } = "Cpu";
        public string Comparison { get; set; } = "GreaterThan";
        public int Threshold { get; set; }
        public int DurationMinutes { get; set; }
        public string Severity { get; set; } = "Medium";
        public bool Enabled { get; set; } = true;
    }

    public sealed class AlertRuleValidationError
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class AlertRuleEvaluationResult
    {
        public int RulesEvaluated { get; set; }
        public int AlertsCreated { get; set; }
        public int AlertsSkipped { get; set; }
        public int AlertsResolved { get; set; }
        public string EvaluatedAtUtc { get; set; } = string.Empty;
        public List<string> CreatedAlertIds { get; set; } = new List<string>();
        public List<AlertSummary> CreatedAlerts { get; set; } = new List<AlertSummary>();
    }

    public sealed class DevicePolicyAssignment
    {
        public string Id { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string PolicyId { get; set; } = string.Empty;
        public string State { get; set; } = "Pending";
        public string EnforcedBy { get; set; } = string.Empty;
        public string EnforcedAt { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class PolicyUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft";
    }

    public sealed class DeviceQuery
    {
        public string Search { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public bool ManagedOnly { get; set; }
        public string SortBy { get; set; } = "Hostname";
        public bool Descending { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public sealed class PagedResult<TEntity>
    {
        public List<TEntity> Items { get; set; } = new List<TEntity>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int PageCount { get; set; }
    }

    public sealed class AuthenticationResult
    {
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = "Operator";
        public string Token { get; set; } = string.Empty;
    }

    public sealed class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public sealed class MobilePairingRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
    }

    public sealed class DeviceEnrollmentRequest
    {
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    public sealed class DeviceEnrollmentResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string Status { get; set; } = "Managed";
        public bool IsManaged { get; set; } = true;
        public string PolicyState { get; set; } = "Pending";
        public string EnrollmentToken { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = string.Empty;
        public bool ReEnrolled { get; set; }
    }

    public sealed class MobilePairingResponse
    {
        public string PairingCode { get; set; } = string.Empty;
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5103;
        public string Fingerprint { get; set; } = "SHA256:MDM-LOCAL";
        public string ExpiresAt { get; set; } = string.Empty;
    }

    public sealed class MobilePairingRecord
    {
        public string Id { get; set; } = string.Empty;
        public string PairingCode { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string EnrollmentToken { get; set; } = string.Empty;
        public string ExpiresAt { get; set; } = string.Empty;
        public bool Used { get; set; }
    }

    public sealed class MobilePairCompleteRequest
    {
        public string PairingCode { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    public sealed class MobilePairCompleteResponse
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string EnrollmentToken { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = string.Empty;
    }
}
