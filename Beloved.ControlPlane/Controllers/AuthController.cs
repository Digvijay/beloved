using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Beloved.ControlPlane.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Controllers;

/// <summary>
/// OAuth2 authentication flow:
///   GET /auth/login/github   → redirect to GitHub
///   GET /auth/login/google   → redirect to Google
///   GET /auth/callback/github → exchange code → JWT cookie
///   GET /auth/callback/google → exchange code → JWT cookie
///   POST /auth/token/refresh → exchange refresh token → new access token
///   GET /auth/me             → current user profile from JWT
/// </summary>
[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IOAuthService _oauth;
    private readonly IJwtTokenService _jwt;
    private readonly BelovedDbContext _db;

    public AuthController(IOAuthService oauth, IJwtTokenService jwt, BelovedDbContext db)
    {
        _oauth = oauth;
        _jwt   = jwt;
        _db    = db;
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

    /// <summary>Initiates the GitHub OAuth2 flow.</summary>
    [HttpGet("login/github")]
    public IActionResult LoginGitHub()
    {
        var state = Guid.NewGuid().ToString("N");
        Response.Cookies.Append("oauth_state", state, new Microsoft.AspNetCore.Http.CookieOptions
        {
            HttpOnly = true, Secure = false, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });
        return Redirect(_oauth.BuildGitHubAuthUrl(state));
    }

    /// <summary>GitHub callback — exchanges code for JWT.</summary>
    [HttpGet("callback/github")]
    public async Task<IActionResult> GitHubCallback(
        [FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        // Validate CSRF state
        if (!Request.Cookies.TryGetValue("oauth_state", out var storedState) || storedState != state)
            return BadRequest("Invalid OAuth state. Possible CSRF attack.");

        Response.Cookies.Delete("oauth_state");

        try
        {
            var user = await _oauth.ExchangeGitHubCodeAsync(code, ct);
            return await IssueTokensAsync(user);
        }
        catch (Exception ex)
        {
            return BadRequest($"GitHub OAuth failed: {ex.Message}");
        }
    }

    // ── Google ────────────────────────────────────────────────────────────────

    /// <summary>Initiates the Google OAuth2 flow.</summary>
    [HttpGet("login/google")]
    public IActionResult LoginGoogle()
    {
        var state = Guid.NewGuid().ToString("N");
        Response.Cookies.Append("oauth_state", state, new Microsoft.AspNetCore.Http.CookieOptions
        {
            HttpOnly = true, Secure = false, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });
        return Redirect(_oauth.BuildGoogleAuthUrl(state));
    }

    /// <summary>Google callback — exchanges code for JWT.</summary>
    [HttpGet("callback/google")]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("oauth_state", out var storedState) || storedState != state)
            return BadRequest("Invalid OAuth state.");

        Response.Cookies.Delete("oauth_state");

        try
        {
            var redirectUri = $"{Request.Scheme}://{Request.Host}/auth/callback/google";
            var user = await _oauth.ExchangeGoogleCodeAsync(code, redirectUri, ct);
            return await IssueTokensAsync(user);
        }
        catch (Exception ex)
        {
            return BadRequest($"Google OAuth failed: {ex.Message}");
        }
    }

    // ── Token Refresh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Exchanges a valid refresh token for a new access token.
    /// Implements refresh token rotation — old token is invalidated immediately.
    /// </summary>
    [HttpPost("token/refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest("Refresh token is required.");

        var record = await _db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == request.RefreshToken && !r.IsRevoked, ct);

        if (record == null || record.ExpiresAt < DateTime.UtcNow)
            return Unauthorized("Refresh token is invalid or expired.");

        // Rotate — revoke old, issue new
        record.IsRevoked = true;
        var newRefreshToken = _jwt.CreateRefreshToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId    = record.UserId,
            Token     = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            User      = record.User
        });
        await _db.SaveChangesAsync(ct);

        var orgSlugs = await _db.OrganisationMembers
            .Where(m => m.UserId == record.UserId)
            .Select(m => m.Organisation!.Slug)
            .ToListAsync(ct);

        var accessToken = _jwt.CreateAccessToken(record.User!, orgSlugs);

        return Ok(new
        {
            accessToken,
            refreshToken = newRefreshToken,
            expiresIn    = 900 // 15 minutes in seconds
        });
    }

    // ── Me ────────────────────────────────────────────────────────────────────

    /// <summary>Returns the authenticated user's profile. Requires Bearer token.</summary>
    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = "JwtBearer")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null) return NotFound();

        var orgs = await _db.OrganisationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Organisation!.Slug, m.Organisation.Name, m.Role })
            .ToListAsync(ct);

        return Ok(new
        {
            id          = user.Id,
            email       = user.Email,
            displayName = user.DisplayName,
            avatarUrl   = user.AvatarUrl,
            provider    = user.Provider,
            orgs
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IActionResult> IssueTokensAsync(BelovedUser user)
    {
        var orgSlugs = await _db.OrganisationMembers
            .Where(m => m.UserId == user.Id)
            .Select(m => m.Organisation!.Slug)
            .ToListAsync();

        var accessToken  = _jwt.CreateAccessToken(user, orgSlugs);
        var refreshToken = _jwt.CreateRefreshToken();

        // Persist refresh token (30 days, rotated on each use)
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId    = user.Id,
            Token     = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            User      = user
        });
        await _db.SaveChangesAsync();

        // Return tokens — in production these would be httpOnly cookies
        // For API-first design we return them in JSON body (SPA handles storage)
        return Ok(new
        {
            accessToken,
            refreshToken,
            expiresIn   = 900,
            user = new
            {
                id          = user.Id,
                email       = user.Email,
                displayName = user.DisplayName,
                avatarUrl   = user.AvatarUrl,
                provider    = user.Provider
            }
        });
    }
}

/// <summary>Request body for POST /auth/token/refresh.</summary>
public sealed record RefreshTokenRequest(string RefreshToken);
