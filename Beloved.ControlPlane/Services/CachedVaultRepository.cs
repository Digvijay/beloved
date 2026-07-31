using Beloved.AssemblyEngine;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Decorator that wraps an IVaultRepository to add cache-aside memory caching for artifact payloads.
/// Manifests and layer blobs in OCI are content-addressable and immutable, making them safe to cache indefinitely.
/// This prevents redundant network roundtrips to the registry during concurrent assemblies.
/// </summary>
public sealed class CachedVaultRepository : IVaultRepository
{
    private readonly IVaultRepository _inner;
    private readonly IMemoryCache _cache;

    public CachedVaultRepository(IVaultRepository inner, IMemoryCache cache)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory)
    {
        var cacheKey = $"vault:template:{templateName}";
        if (_cache.TryGetValue(cacheKey, out (string, string) cachedResult))
        {
            if (Directory.Exists(cachedResult.Item1))
            {
                CopyDirectory(cachedResult.Item1, targetDirectory);
                return (targetDirectory, cachedResult.Item2);
            }
        }

        var result = await _inner.FetchTemplateAsync(templateName, targetDirectory);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        return result;
    }

    public async Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory, CancellationToken cancellationToken)
    {
        var cacheKey = $"vault:template:{templateName}";
        if (_cache.TryGetValue(cacheKey, out (string, string) cachedResult))
        {
            if (Directory.Exists(cachedResult.Item1))
            {
                CopyDirectory(cachedResult.Item1, targetDirectory);
                return (targetDirectory, cachedResult.Item2);
            }
        }

        var result = await _inner.FetchTemplateAsync(templateName, targetDirectory, cancellationToken);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        return result;
    }

    public async Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory)
    {
        var cacheKey = $"vault:module:{moduleName}:{version}";
        if (_cache.TryGetValue(cacheKey, out (string, string) cachedResult))
        {
            if (Directory.Exists(cachedResult.Item1))
            {
                CopyDirectory(cachedResult.Item1, targetDirectory);
                return (targetDirectory, cachedResult.Item2);
            }
        }

        var result = await _inner.FetchModuleAsync(moduleName, version, targetDirectory);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        return result;
    }

    public async Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory, CancellationToken cancellationToken)
    {
        var cacheKey = $"vault:module:{moduleName}:{version}";
        if (_cache.TryGetValue(cacheKey, out (string, string) cachedResult))
        {
            if (Directory.Exists(cachedResult.Item1))
            {
                CopyDirectory(cachedResult.Item1, targetDirectory);
                return (targetDirectory, cachedResult.Item2);
            }
        }

        var result = await _inner.FetchModuleAsync(moduleName, version, targetDirectory, cancellationToken);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        return result;
    }

    public async Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName)
    {
        var cacheKey = $"vault:template:mem:{templateName}";
        if (_cache.TryGetValue(cacheKey, out (Dictionary<string, byte[]> files, string digest) cachedResult))
        {
            var cachedDict = cachedResult.files != null ? new Dictionary<string, byte[]>(cachedResult.files) : new Dictionary<string, byte[]>();
            return (cachedDict, cachedResult.digest);
        }

        var result = await _inner.FetchTemplateInMemoryAsync(templateName);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
        return (resDict, result.digest);
    }

    public async Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName, CancellationToken cancellationToken)
    {
        var cacheKey = $"vault:template:mem:{templateName}";
        if (_cache.TryGetValue(cacheKey, out (Dictionary<string, byte[]> files, string digest) cachedResult))
        {
            var cachedDict = cachedResult.files != null ? new Dictionary<string, byte[]>(cachedResult.files) : new Dictionary<string, byte[]>();
            return (cachedDict, cachedResult.digest);
        }

        var result = await _inner.FetchTemplateInMemoryAsync(templateName, cancellationToken);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
        return (resDict, result.digest);
    }

    public async Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version)
    {
        var cacheKey = $"vault:module:mem:{moduleName}:{version}";
        if (_cache.TryGetValue(cacheKey, out (Dictionary<string, byte[]> files, string digest) cachedResult))
        {
            var cachedDict = cachedResult.files != null ? new Dictionary<string, byte[]>(cachedResult.files) : new Dictionary<string, byte[]>();
            return (cachedDict, cachedResult.digest);
        }

        var result = await _inner.FetchModuleInMemoryAsync(moduleName, version);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
        return (resDict, result.digest);
    }

    public async Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version, CancellationToken cancellationToken)
    {
        var cacheKey = $"vault:module:mem:{moduleName}:{version}";
        if (_cache.TryGetValue(cacheKey, out (Dictionary<string, byte[]> files, string digest) cachedResult))
        {
            var cachedDict = cachedResult.files != null ? new Dictionary<string, byte[]>(cachedResult.files) : new Dictionary<string, byte[]>();
            return (cachedDict, cachedResult.digest);
        }

        var result = await _inner.FetchModuleInMemoryAsync(moduleName, version, cancellationToken);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
        return (resDict, result.digest);
    }

    public Task PushModuleAsync(string modulePath, string moduleName, string version)
    {
        var cacheKey = $"vault:module:{moduleName}:{version}";
        _cache.Remove(cacheKey);
        return _inner.PushModuleAsync(modulePath, moduleName, version);
    }

    public Task PushModuleAsync(string modulePath, string moduleName, string version, CancellationToken cancellationToken)
    {
        var cacheKey = $"vault:module:{moduleName}:{version}";
        _cache.Remove(cacheKey);
        return _inner.PushModuleAsync(modulePath, moduleName, version, cancellationToken);
    }

    public Task<IEnumerable<string>> ListModulesAsync()
    {
        return _inner.ListModulesAsync();
    }

    public Task<IEnumerable<string>> ListModulesAsync(CancellationToken cancellationToken)
    {
        return _inner.ListModulesAsync(cancellationToken);
    }

    public Task<bool> VerifySignatureAsync(string moduleName, string version)
    {
        return _inner.VerifySignatureAsync(moduleName, version);
    }

    public Task<bool> VerifySignatureAsync(string moduleName, string version, CancellationToken cancellationToken)
    {
        return _inner.VerifySignatureAsync(moduleName, version, cancellationToken);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }
}
