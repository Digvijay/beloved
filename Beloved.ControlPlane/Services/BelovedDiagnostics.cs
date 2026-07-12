using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Beloved.ControlPlane.Services;

public static class BelovedDiagnostics
{
    public static readonly ActivitySource Source = new("Beloved.AssemblyEngine");
    public static readonly Meter Meter = new("Beloved.AssemblyEngine");
    
    public static readonly Counter<long> AssemblyCompletedCounter = Meter.CreateCounter<long>(
        "beloved_assemblies_completed_total",
        description: "Total number of successfully completed app assemblies");

    public static readonly Counter<long> AssemblyFailedCounter = Meter.CreateCounter<long>(
        "beloved_assemblies_failed_total",
        description: "Total number of failed app assemblies");

    /// <summary>
    /// Per-tenant assembly counter. Tag with KeyValuePair("tenant_id", tenantId.ToString()).
    /// Powers per-tenant billing dashboards and quota alerting in Grafana.
    /// </summary>
    public static readonly Counter<long> TenantAssembliesCounter = Meter.CreateCounter<long>(
        "beloved_tenant_assemblies_total",
        description: "Assembly count broken down by tenant_id label — used for billing metering");
}
