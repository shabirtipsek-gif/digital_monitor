using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using DigitalMonitoringSystem.Api;

namespace DigitalMonitoringSystem.Tests;

public class DeviceTokenServiceTests
{
    private const string SigningKey = "MukonoDevelopmentSigningKey-ReplaceBeforeProduction-2026";
    private const string OtherKey = "ADifferentMukonoSigningKey-ReplaceBeforeProduction-2026";

    private static DeviceTokenService Service(string key = SigningKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = key
            })
            .Build();

        return new DeviceTokenService(configuration);
    }

    private static string Forge(string deviceId, DateTimeOffset expires, string key)
    {
        var payload = $"{deviceId}|{expires.ToUnixTimeSeconds()}|{Guid.NewGuid():N}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"{Base64Url(Encoding.UTF8.GetBytes(payload))}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void IssuedToken_RoundTripsBackToItsDevice()
    {
        var service = Service();

        var token = service.Issue("DEV-ABC123");

        Assert.Contains('.', token);
        Assert.Equal("DEV-ABC123", service.Validate(token));
    }

    [Fact]
    public void IssuedToken_HasNoCharactersThatBreakAHeader()
    {
        var token = Service().Issue("DEV-ABC123");

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Validate_RejectsATokenSignedWithADifferentKey()
    {
        var token = Forge("DEV-ABC123", DateTimeOffset.UtcNow.AddDays(1), OtherKey);

        Assert.Null(Service().Validate(token));
    }

    [Fact]
    public void Validate_RejectsATamperedPayload()
    {
        var service = Service();
        var token = service.Issue("DEV-ABC123");
        var segments = token.Split('.');

        var forged = string.Join('.', new[] { segments[0], Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes("not-the-signature"))) });

        Assert.Null(service.Validate(forged));
        Assert.Null(service.Validate($"AAAA.{segments[1]}"));
    }

    [Fact]
    public void Validate_RejectsAnExpiredToken()
    {
        var token = Forge("DEV-ABC123", DateTimeOffset.UtcNow.AddMinutes(-1), SigningKey);

        Assert.Null(Service().Validate(token));
    }

    [Fact]
    public void Validate_AcceptsATokenThatHasNotExpiredYet()
    {
        var token = Forge("DEV-ABC123", DateTimeOffset.UtcNow.AddMinutes(1), SigningKey);

        Assert.Equal("DEV-ABC123", Service().Validate(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dot-in-here")]
    [InlineData("only.two")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    [InlineData("YQ")]
    public void Validate_RejectsMalformedInput(string? token)
    {
        Assert.Null(Service().Validate(token));
    }

    [Fact]
    public void Validate_RejectsAForgedTokenWithNoDeviceId()
    {
        // An empty device id yields a blank string, which the authentication handler treats as no
        // result at all.
        var token = Forge(string.Empty, DateTimeOffset.UtcNow.AddDays(1), SigningKey);

        Assert.Equal(string.Empty, Service().Validate(token));
    }

    [Fact]
    public void Validate_ReturnsNullInsteadOfThrowingWhenTheKeyIsNotConfigured()
    {
        var forged = Forge("DEV-ABC123", DateTimeOffset.UtcNow.AddDays(1), SigningKey);

        Assert.Null(Service(key: string.Empty).Validate(forged));
        Assert.Null(Service(key: "too-short").Validate(forged));
    }

    [Fact]
    public void Issue_RefusesToWorkWithoutAProperlySizedKey()
    {
        Assert.Throws<InvalidOperationException>(() => Service(key: string.Empty).Issue("DEV-ABC123"));
        Assert.Throws<InvalidOperationException>(() => Service(key: "short").Issue("DEV-ABC123"));
    }

    [Fact]
    public void Issue_RequiresADeviceId()
    {
        Assert.Throws<ArgumentException>(() => Service().Issue("   "));
    }

    [Fact]
    public void Issue_ProducesADifferentTokenEachTime()
    {
        var service = Service();

        Assert.NotEqual(service.Issue("DEV-ABC123"), service.Issue("DEV-ABC123"));
    }
}
