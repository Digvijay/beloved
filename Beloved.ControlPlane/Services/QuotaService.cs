using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Checks and records assembly quota consumption for a tenant.
/// Written to the Fowler/Edwards standard — thin, focused, injectable.
/// </summary>
public interface IQuotaService
{
    /// <summary>Returns true if the tenant still has quota remaining this month.</summary>
    Task<bool> HasQuotaAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Returns the number of assemblies the tenant has used this calendar month.</summary>
    Task<int> GetUsedThisMonthAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Records a completed assembly against the tenant's quota.</summary>
    Task RecordUsageAsync(Guid tenantId, string jobId, long durationMs, int moduleCount, bool succeeded, CancellationToken ct = default);
}

public sealed class QuotaService : IQuotaService
{
    private readonly BelovedDbContext _db;

    public QuotaService(BelovedDbContext db) => _db = db;

    private static string CurrentPeriod() => DateTime.UtcNow.ToString("yyyy-MM");

    public async Task<bool> HasQuotaAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant == null) return false;
        if (tenant.Plan == TenantPlan.Enterprise) return true;

        var used = await GetUsedThisMonthAsync(tenantId, ct);
        return used < tenant.MonthlyQuota;
    }

    public async Task<int> GetUsedThisMonthAsync(Guid tenantId, CancellationToken ct = default)
    {
        var period = CurrentPeriod();
        return await _db.AssemblyUsages
            .AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId && u.PeriodMonth == period, ct);
    }

    public async Task RecordUsageAsync(Guid tenantId, string jobId, long durationMs, int moduleCount, bool succeeded, CancellationToken ct = default)
    {
        _db.AssemblyUsages.Add(new AssemblyUsage
        {
            TenantId  = tenantId,
            JobId     = jobId,
            DurationMs  = durationMs,
            ModuleCount = moduleCount,
            Succeeded   = succeeded,
            PeriodMonth = CurrentPeriod()
        });

        await _db.SaveChangesAsync(ct);
    }
}
