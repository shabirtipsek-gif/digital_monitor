using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class MobilePairingService
{
    private readonly MdmDbContext _context;
    private readonly DeviceTokenService _tokens;

    public MobilePairingService(MdmDbContext context, DeviceTokenService tokens)
    {
        _context = context;
        _tokens = tokens;
    }

    public MobilePairingRecord CreateCode(string deviceName, string platform)
    {
        var code = $"MDM-{RandomNumberGenerator.GetInt32(100000, 1000000)}";
        var record = new MobilePairingRecord
        {
            Id = $"PAIR-{Guid.NewGuid():N}".ToUpperInvariant(),
            PairingCode = code,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Mobile Device" : deviceName.Trim(),
            Platform = string.IsNullOrWhiteSpace(platform) ? "Android" : platform.Trim(),
            ExpiresAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(10)),
            Used = false
        };

        _context.MobilePairings.Add(record);
        _context.SaveChanges();
        return record;
    }

    public (MobilePairCompleteResponse? Response, string? Error) Complete(string code, string ipAddress, string location, string apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return (null, "Pairing code is required.");
        }

        var record = _context.MobilePairings.FirstOrDefault(x => x.PairingCode == code.Trim().ToUpperInvariant());
        if (record is null)
        {
            return (null, "Pairing code was not found.");
        }

        if (record.Used)
        {
            return (null, "Pairing code has already been used.");
        }

        if (!DateTimeOffset.TryParse(record.ExpiresAt, out var expires) || expires <= DateTimeOffset.UtcNow)
        {
            return (null, "Pairing code has expired.");
        }

        var device = new DeviceRecord
        {
            Id = $"DEV-{Guid.NewGuid():N}".ToUpperInvariant(),
            Hostname = record.DeviceName,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? "Unknown" : ipAddress.Trim(),
            Vendor = "Mobile",
            Platform = record.Platform,
            Location = string.IsNullOrWhiteSpace(location) ? "Mobile" : location.Trim(),
            Status = "Online",
            IsManaged = true,
            Cpu = 0,
            Memory = 0,
            LastSeen = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow)
        };

        _context.Devices.Add(device);
        record.DeviceId = device.Id;
        record.EnrollmentToken = _tokens.Issue(device.Id);
        record.Used = true;
        _context.SaveChanges();

        return (new MobilePairCompleteResponse
        {
            DeviceId = device.Id,
            DeviceName = device.Hostname,
            EnrollmentToken = record.EnrollmentToken,
            ApiBaseUrl = apiBaseUrl.TrimEnd('/')
        }, null);
    }
}
