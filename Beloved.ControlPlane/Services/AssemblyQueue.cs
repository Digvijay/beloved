using Beloved.AssemblyEngine;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;

namespace Beloved.ControlPlane.Services;

public record AssemblyJob(string JobId, Blueprint Blueprint);

public interface IAssemblyQueue
{
    ValueTask QueueJobAsync(AssemblyJob job, CancellationToken cancellationToken = default);
    ValueTask<AssemblyJob> DequeueAsync(CancellationToken cancellationToken = default);
}

public class AssemblyQueue : IAssemblyQueue
{
    private readonly Channel<AssemblyJob> _queue;

    public AssemblyQueue()
    {
        var options = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<AssemblyJob>(options);
    }

    public async ValueTask QueueJobAsync(AssemblyJob job, CancellationToken cancellationToken = default)
    {
        await _queue.Writer.WriteAsync(job, cancellationToken);
    }

    public async ValueTask<AssemblyJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
