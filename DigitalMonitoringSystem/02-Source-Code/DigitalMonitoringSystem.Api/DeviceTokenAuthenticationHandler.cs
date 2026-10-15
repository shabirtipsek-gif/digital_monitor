using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class DeviceTokenSchemeOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Device-Token";
}

public sealed class DeviceTokenAuthenticationHandler : AuthenticationHandler<DeviceTokenSchemeOptions>
{
    public const string SchemeName = "DeviceToken";

    public DeviceTokenAuthenticationHandler(
        IOptionsMonitor<DeviceTokenSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var tokens = Context.RequestServices.GetRequiredService<DeviceTokenService>();
        var deviceId = tokens.Validate(token);
        if (string.IsNullOrEmpty(deviceId))
        {
            return Task.FromResult(AuthenticateResult.Fail("The device token is not valid."));
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, deviceId),
                new Claim(ClaimTypes.Name, deviceId),
                new Claim(ClaimTypes.Role, DeviceTokenService.Role)
            },
            Scheme.Name);

        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    private string? ReadToken()
    {
        if (Request.Headers.TryGetValue(Options.HeaderName, out var headerValues))
        {
            var headerToken = headerValues.ToString().Trim();
            if (headerToken.Length > 0)
            {
                return headerToken;
            }
        }

        // Also accept the Authorization: Device <token> form so agents can reuse their normal
        // bearer plumbing.
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        var match = Regex.Match(authorization, @"^Device\s+(?<token>\S+)$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["token"].Value : null;
    }
}
