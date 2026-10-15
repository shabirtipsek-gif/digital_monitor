using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class AuthService
{
    private readonly IConfiguration _configuration;

    public AuthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public AuthenticationResult? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var normalizedUsername = username.Trim();
        var configuredUsername = _configuration["Admin:Username"];
        var configuredPassword = _configuration["Admin:Password"];
        var isValidAdministrator = !string.IsNullOrWhiteSpace(configuredUsername)
            && !string.IsNullOrWhiteSpace(configuredPassword)
            && normalizedUsername.Equals(configuredUsername, StringComparison.OrdinalIgnoreCase)
            && string.Equals(password, configuredPassword, StringComparison.Ordinal);

        if (!isValidAdministrator)
        {
            return null;
        }

        var key = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Configure Jwt:Key with at least 32 bytes before issuing tokens.");
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, normalizedUsername),
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim(JwtRegisteredClaimNames.Sub, normalizedUsername),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: "DigitalMonitoringSystem.Api",
            audience: "DigitalMonitoringSystem.Client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: signingCredentials);

        return new AuthenticationResult
        {
            Username = normalizedUsername,
            Role = "Administrator",
            Token = tokenHandler.WriteToken(token)
        };
    }
}
