using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Handles GitHub and Google OAuth2 code exchange and upserts the resulting
/// identity into the BelovedUser table.
/// Stateless — all state is in the DB and JWT. Fowler/Edwards standard.
/// </summary>
public interface IOAuthService
{
    /// <summary>Builds the GitHub authorization URL to redirect the browser to.</summary>
    string BuildGitHubAuthUrl(string state);

    /// <summary>Builds the Google authorization URL to redirect the browser to.</summary>
    string BuildGoogleAuthUrl(string state);

    /// <summary>Exchanges a GitHub OAuth code for a BelovedUser, upserting in DB.</summary>
    Task<BelovedUser> ExchangeGitHubCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Exchanges a Google OAuth code for a BelovedUser, upserting in DB.</summary>
    Task<BelovedUser> ExchangeGoogleCodeAsync(string code, string redirectUri, CancellationToken ct = default);
}

public sealed class OAuthService : IOAuthService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly BelovedDbContext _db;
    private readonly IConfiguration _config;

    public OAuthService(IHttpClientFactory httpFactory, BelovedDbContext db, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _db          = db;
        _config      = config;
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

    public string BuildGitHubAuthUrl(string state)
    {
        var clientId = _config["OAuth:GitHub:ClientId"] ?? string.Empty;
        var redirectUri = _config["OAuth:GitHub:RedirectUri"] ?? string.Empty;
        return $"https://github.com/login/oauth/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=user:email&state={state}";
    }

    public async Task<BelovedUser> ExchangeGitHubCodeAsync(string code, CancellationToken ct = default)
    {
        var clientId     = _config["OAuth:GitHub:ClientId"];
        var clientSecret = _config["OAuth:GitHub:ClientSecret"];
        var redirectUri  = _config["OAuth:GitHub:RedirectUri"];

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        http.DefaultRequestHeaders.Add("User-Agent", "beloved.build");

        // Step 1: Exchange code for access token
        var tokenResp = await http.PostAsJsonAsync("https://github.com/login/oauth/access_token", new
        {
            client_id     = clientId,
            client_secret = clientSecret,
            code,
            redirect_uri  = redirectUri
        }, ct);

        tokenResp.EnsureSuccessStatusCode();
        var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("GitHub did not return an access_token");

        // Step 2: Fetch user profile
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var profileResp = await http.GetFromJsonAsync<GitHubUserDto>("https://api.github.com/user", ct)
            ?? throw new InvalidOperationException("GitHub user profile returned null");

        // Step 3: Fetch primary verified email if not in profile
        var email = profileResp.Email;
        if (string.IsNullOrEmpty(email))
        {
            var emails = await http.GetFromJsonAsync<GitHubEmailDto[]>("https://api.github.com/user/emails", ct)
                ?? Array.Empty<GitHubEmailDto>();
            email = Array.Find(emails, e => e.Primary && e.Verified)?.Email
                ?? profileResp.Login + "@github.invalid";
        }

        return await UpsertUserAsync("github", profileResp.Id.ToString(), email, profileResp.Name ?? profileResp.Login, profileResp.AvatarUrl, ct);
    }

    // ── Google ────────────────────────────────────────────────────────────────

    public string BuildGoogleAuthUrl(string state)
    {
        var clientId    = _config["OAuth:Google:ClientId"] ?? string.Empty;
        var redirectUri = _config["OAuth:Google:RedirectUri"] ?? string.Empty;
        return $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope=openid+email+profile&state={state}&access_type=offline";
    }

    public async Task<BelovedUser> ExchangeGoogleCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var clientId     = _config["OAuth:Google:ClientId"];
        var clientSecret = _config["OAuth:Google:ClientSecret"];
        var configRedirectUri = _config["OAuth:Google:RedirectUri"] ?? redirectUri;

        var http = _httpFactory.CreateClient();

        // Step 1: Exchange code for tokens
        var tokenResp = await http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = clientId ?? string.Empty,
                ["client_secret"] = clientSecret ?? string.Empty,
                ["code"]          = code,
                ["redirect_uri"]  = configRedirectUri,
                ["grant_type"]    = "authorization_code"
            }), ct);

        tokenResp.EnsureSuccessStatusCode();
        var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var idToken = tokenDoc.RootElement.GetProperty("id_token").GetString()
            ?? throw new InvalidOperationException("Google did not return an id_token");

        // Step 2: Decode ID token (we trust Google, no verification needed for the profile exchange)
        var userInfoResp = await http.GetFromJsonAsync<GoogleUserDto>(
            $"https://www.googleapis.com/oauth2/v3/userinfo",
            cancellationToken: ct)
            ?? throw new InvalidOperationException("Google userinfo returned null");

        // Set bearer token for userinfo
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", tokenDoc.RootElement.GetProperty("access_token").GetString());
        userInfoResp = await http.GetFromJsonAsync<GoogleUserDto>(
            "https://www.googleapis.com/oauth2/v3/userinfo", ct)
            ?? throw new InvalidOperationException("Google userinfo returned null");

        return await UpsertUserAsync("google", userInfoResp.Sub, userInfoResp.Email, userInfoResp.Name, userInfoResp.Picture, ct);
    }

    // ── Upsert ────────────────────────────────────────────────────────────────

    private async Task<BelovedUser> UpsertUserAsync(
        string provider, string subject, string email,
        string? displayName, string? avatarUrl, CancellationToken ct)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Provider == provider && u.ProviderSubject == subject, ct);

        if (user == null)
        {
            user = new BelovedUser
            {
                Provider        = provider,
                ProviderSubject = subject,
                Email           = email,
                DisplayName     = displayName,
                AvatarUrl       = avatarUrl
            };
            _db.Users.Add(user);
        }
        else
        {
            user.Email       = email;
            user.DisplayName = displayName;
            user.AvatarUrl   = avatarUrl;
            user.LastLoginAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return user;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private sealed record GitHubUserDto(
        [property: JsonPropertyName("id")]         long Id,
        [property: JsonPropertyName("login")]      string Login,
        [property: JsonPropertyName("name")]       string? Name,
        [property: JsonPropertyName("email")]      string? Email,
        [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

    private sealed record GitHubEmailDto(
        [property: JsonPropertyName("email")]    string Email,
        [property: JsonPropertyName("primary")]  bool Primary,
        [property: JsonPropertyName("verified")] bool Verified);

    private sealed record GoogleUserDto(
        [property: JsonPropertyName("sub")]     string Sub,
        [property: JsonPropertyName("email")]   string Email,
        [property: JsonPropertyName("name")]    string Name,
        [property: JsonPropertyName("picture")] string? Picture);
}
