using Beloved.ControlPlane.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Issues and validates JWT access tokens for Beloved users.
/// Tokens are HS256-signed short-lived JWTs (15 minutes).
/// Written to the Fowler/Edwards standard — a single, composable, injectable service.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Creates a signed JWT access token for the given user.</summary>
    string CreateAccessToken(BelovedUser user, IEnumerable<string>? orgSlugs = null);

    /// <summary>Generates a cryptographically random refresh token string.</summary>
    string CreateRefreshToken();
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenMinutes;

    public JwtTokenService(IConfiguration config)
    {
        var jwtConfig = config.GetSection("Jwt");
        var secret = jwtConfig["Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is required in appsettings.json");

        _signingKey       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer           = jwtConfig["Issuer"]   ?? "beloved.build";
        _audience         = jwtConfig["Audience"] ?? "beloved.build";
        _accessTokenMinutes = int.TryParse(jwtConfig["AccessTokenMinutes"], out var m) ? m : 15;
    }

    public string CreateAccessToken(BelovedUser user, IEnumerable<string>? orgSlugs = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name,  user.DisplayName ?? user.Email),
            new("provider", user.Provider),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };

        if (orgSlugs != null)
            foreach (var slug in orgSlugs)
                claims.Add(new Claim("org", slug));

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:   _issuer,
            audience: _audience,
            claims:   claims,
            notBefore: DateTime.UtcNow,
            expires:   DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}
