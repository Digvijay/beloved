using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Beloved.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Dispatches webhook events to all active, matching tenant endpoints.
/// Uses HMAC-SHA256 signing (X-Beloved-Signature header) for payload integrity.
/// Each dispatch is independent — one failing endpoint never blocks others.
/// </summary>
public sealed class WebhookDispatcher : IWebhookDispatcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookDispatcher> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookDispatcher(
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider,
        ILogger<WebhookDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid tenantId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();

        var webhooks = await db.Webhooks
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .ToListAsync(cancellationToken);

        if (webhooks.Count == 0) return;

        var envelope = new
        {
            id = Guid.NewGuid().ToString("N"),
            @event = eventType,
            timestamp = DateTime.UtcNow,
            data = payload
        };

        var body = JsonSerializer.Serialize(envelope, JsonOptions);
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        // Dispatch all matching webhooks concurrently — failures are isolated
        var tasks = webhooks
            .Where(w => string.IsNullOrEmpty(w.Events) || w.Events.Split(',').Contains(eventType))
            .Select(webhook => FireAsync(webhook.Url, webhook.Secret, body, bodyBytes, eventType, cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task FireAsync(string url, string secret, string body, byte[] bodyBytes, string eventType, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WebhookClient");
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            // HMAC-SHA256 signature so receivers can verify authenticity
            if (!string.IsNullOrEmpty(secret))
            {
                var signature = ComputeHmac(secret, bodyBytes);
                request.Headers.Add("X-Beloved-Signature", $"sha256={signature}");
            }

            request.Headers.Add("X-Beloved-Event", eventType);
            request.Headers.Add("X-Beloved-Agent", "Beloved/1.0");

            var response = await client.SendAsync(request, cancellationToken);
            _logger.LogInformation("Webhook dispatched to {Url} — HTTP {Status}", url, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Never throw — a bad consumer endpoint must not break the assembly pipeline
            _logger.LogWarning(ex, "Webhook dispatch failed for {Url}", url);
        }
    }

    private static string ComputeHmac(string secret, byte[] payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
