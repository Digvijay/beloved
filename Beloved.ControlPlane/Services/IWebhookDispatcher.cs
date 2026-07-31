using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Dispatches assembly lifecycle events to tenant-registered HTTP webhook endpoints.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>
    /// Fire-and-forget: dispatch an event to all active webhooks registered for this tenant.
    /// </summary>
    Task DispatchAsync(Guid tenantId, string eventType, object payload, CancellationToken cancellationToken = default);
}
