using Beloved.AssemblyEngine;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Stores assembly artifacts to local disk.
/// A per-job SemaphoreSlim ensures that if two workers accidentally share a jobId,
/// the write is serialised rather than corrupting the file. Under normal operation
/// each jobId is unique, so the semaphore adds zero contention.
/// </summary>
public class LocalDiskOutputStore : IOutputStore
{
    private readonly string _storageDir;

    // Keyed by jobId — created lazily, so only jobs that are actually writing hold a semaphore
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public LocalDiskOutputStore()
    {
        _storageDir = Path.Combine(Directory.GetCurrentDirectory(), ".beloved_artifacts");
        Directory.CreateDirectory(_storageDir);
    }

    public async Task<string> StoreArtifactAsync(string jobId, Stream artifactStream)
    {
        var filePath = Path.Combine(_storageDir, $"{jobId}.zip");

        // Acquire a per-job write lock — protects against accidental concurrent writes for the same jobId
        var sem = _locks.GetOrAdd(jobId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            using var fileStream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,   // 80 KB buffer — reduces syscall count for large zips
                useAsync: true);

            await artifactStream.CopyToAsync(fileStream).ConfigureAwait(false);
            return filePath;
        }
        finally
        {
            sem.Release();
            // Clean up the semaphore once the write is complete to avoid unbounded growth
            _locks.TryRemove(jobId, out _);
        }
    }

    public Task<Stream?> GetArtifactAsync(string jobId)
    {
        var filePath = Path.Combine(_storageDir, $"{jobId}.zip");
        if (File.Exists(filePath))
        {
            // Async read with buffered stream — safe for concurrent callers (FileShare.Read)
            return Task.FromResult<Stream?>(new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                useAsync: true));
        }
        return Task.FromResult<Stream?>(null);
    }
}
