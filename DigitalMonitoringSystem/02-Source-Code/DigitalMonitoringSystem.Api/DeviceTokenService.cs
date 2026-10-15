using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace DigitalMonitoringSystem.Api;

/// <summary>
/// Issues the bearer token a managed device uses to post telemetry. Administrators already have a
/// JWT, so agents get a narrower token that only identifies the device, is signed with the same
/// secret, and stops working once it expires.
/// </summary>
public sealed class DeviceTokenService
{
    public const string Role = "Device";
    public const string AuthorizationPrefix = "Device";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    private const string SEPARATOR = "|";

    private readonly IConfiguration _configuration;

    public DeviceTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Issue(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("A device id is required to issue a token.", nameof(deviceId));
        }

        // The nonce keeps two tokens issued to the same device within the same second distinct.
        var payload = string.Join(SEPARATOR,
            deviceId.Trim(),
            DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Guid.NewGuid().ToString("N"));

        return string.Concat(Encode(payload), ".", ToBase64Url(Sign(payload)));
    }

    /// <summary>
    /// Returns the device id when the signature and expiry check out, otherwise null.
    /// </summary>
    public string? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Trim().Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        string payload;
        byte[] signature;
        try
        {
            payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        byte[] expected;
        try
        {
            expected = Sign(payload);
        }
        catch (InvalidOperationException)
        {
            // No signing key configured, so no token can be trusted.
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
        {
            return null;
        }

        var segments = payload.Split(SEPARATOR);
        if (segments.Length != 3)
        {
            return null;
        }

        if (!long.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresUnix))
        {
            return null;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return segments[0];
    }

    private byte[] Sign(string payload)
    {
        var key = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Configure Jwt:Key with at least 32 bytes before signing device tokens.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string Encode(string value) => ToBase64Url(Encoding.UTF8.GetBytes(value));

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException("Invalid base64url length.")
        };

        return Convert.FromBase64String(padded);
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
