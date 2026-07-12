using Beloved.ControlPlane.Auth;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Beloved.ControlPlane.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Middleware;

/// <summary>
/// Intercepts POST /api/assemble requests and returns 429 Too Many Requests
/// when the authenticated tenant has exhausted their monthly assembly quota.
/// Must run AFTER authentication middleware so ClaimsPrincipal is populated.
/// </summary>
public sealed class QuotaMiddleware
{
    private readonly RequestDelegate _next;

    public QuotaMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Only gate the assemble endpoint
        if (!context.Request.Path.StartsWithSegments("/api/assemble", StringComparison.OrdinalIgnoreCase)
            || !HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Resolve tenant from API key header (mirrors ApiKeyAuthMiddleware logic)
        var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await _next(context);
            return;
        }

        await using var scope = context.RequestServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();
        var quotaService = scope.ServiceProvider.GetRequiredService<IQuotaService>();

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ApiKey == apiKey);

        if (tenant == null)
        {
            await _next(context);
            return;
        }

        // Enterprise tenants are always allowed through — short-circuit here
        if (tenant.Plan == TenantPlan.Enterprise)
        {
            await _next(context);
            return;
        }

        var hasQuota = await quotaService.HasQuotaAsync(tenant.Id, context.RequestAborted);
        if (!hasQuota)
        {
            var used = await quotaService.GetUsedThisMonthAsync(tenant.Id, context.RequestAborted);
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] = GetSecondsUntilNextMonth().ToString();

            var payload = JsonSerializer.Serialize(new
            {
                error     = "quota_exceeded",
                message   = $"Monthly assembly quota reached ({used}/{tenant.MonthlyQuota}). Upgrade to Pro or Enterprise to continue.",
                used      = used,
                quota     = tenant.MonthlyQuota,
                plan      = tenant.Plan.ToString(),
                upgradeUrl = "/api/billing/checkout"
            });

            await context.Response.WriteAsync(payload);
            return;
        }

        await _next(context);
    }

    private static int GetSecondsUntilNextMonth()
    {
        var now = DateTime.UtcNow;
        var nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return (int)(nextMonth - now).TotalSeconds;
    }
}
