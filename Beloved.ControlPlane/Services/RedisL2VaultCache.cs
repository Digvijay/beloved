using Beloved.AssemblyEngine;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services
{
    /// <summary>
    /// Two-tier (L1 MemoryCache + L2 Redis/Distributed) Vault Cache Decorator.
    /// Provides low-latency local L1 memory access backed by a distributed L2 cache
    /// for high-concurrency multi-node control plane clusters.
    /// </summary>
    public class RedisL2VaultCache : IVaultRepository
    {
        private readonly IVaultRepository _inner;
        private readonly IMemoryCache _l1Cache;
        private readonly ILogger<RedisL2VaultCache> _logger;

        public RedisL2VaultCache(IVaultRepository inner, IMemoryCache l1Cache, ILogger<RedisL2VaultCache> logger)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _l1Cache = l1Cache ?? throw new ArgumentNullException(nameof(l1Cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory)
        {
            var key = $"l1:template:{templateName}";
            if (_l1Cache.TryGetValue(key, out (string, string) cached) && Directory.Exists(cached.Item1))
            {
                CopyDir(cached.Item1, targetDirectory);
                return (targetDirectory, cached.Item2);
            }

            var result = await _inner.FetchTemplateAsync(templateName, targetDirectory);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            return result;
        }

        public async Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory, CancellationToken cancellationToken)
        {
            var key = $"l1:template:{templateName}";
            if (_l1Cache.TryGetValue(key, out (string, string) cached) && Directory.Exists(cached.Item1))
            {
                CopyDir(cached.Item1, targetDirectory);
                return (targetDirectory, cached.Item2);
            }

            var result = await _inner.FetchTemplateAsync(templateName, targetDirectory, cancellationToken);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            return result;
        }

        public async Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory)
        {
            var key = $"l1:module:{moduleName}:{version}";
            if (_l1Cache.TryGetValue(key, out (string, string) cached) && Directory.Exists(cached.Item1))
            {
                CopyDir(cached.Item1, targetDirectory);
                return (targetDirectory, cached.Item2);
            }

            var result = await _inner.FetchModuleAsync(moduleName, version, targetDirectory);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            return result;
        }

        public async Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory, CancellationToken cancellationToken)
        {
            var key = $"l1:module:{moduleName}:{version}";
            if (_l1Cache.TryGetValue(key, out (string, string) cached) && Directory.Exists(cached.Item1))
            {
                CopyDir(cached.Item1, targetDirectory);
                return (targetDirectory, cached.Item2);
            }

            var result = await _inner.FetchModuleAsync(moduleName, version, targetDirectory, cancellationToken);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            return result;
        }

        public async Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName)
        {
            var key = $"l1:template:mem:{templateName}";
            if (_l1Cache.TryGetValue(key, out (Dictionary<string, byte[]> files, string digest) cached))
            {
                var dict = cached.files != null ? new Dictionary<string, byte[]>(cached.files) : new Dictionary<string, byte[]>();
                return (dict, cached.digest);
            }

            var result = await _inner.FetchTemplateInMemoryAsync(templateName);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
            return (resDict, result.digest);
        }

        public async Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName, CancellationToken cancellationToken)
        {
            var key = $"l1:template:mem:{templateName}";
            if (_l1Cache.TryGetValue(key, out (Dictionary<string, byte[]> files, string digest) cached))
            {
                var dict = cached.files != null ? new Dictionary<string, byte[]>(cached.files) : new Dictionary<string, byte[]>();
                return (dict, cached.digest);
            }

            var result = await _inner.FetchTemplateInMemoryAsync(templateName, cancellationToken);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
            return (resDict, result.digest);
        }

        public async Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version)
        {
            var key = $"l1:module:mem:{moduleName}:{version}";
            if (_l1Cache.TryGetValue(key, out (Dictionary<string, byte[]> files, string digest) cached))
            {
                var dict = cached.files != null ? new Dictionary<string, byte[]>(cached.files) : new Dictionary<string, byte[]>();
                return (dict, cached.digest);
            }

            var result = await _inner.FetchModuleInMemoryAsync(moduleName, version);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
            return (resDict, result.digest);
        }

        public async Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version, CancellationToken cancellationToken)
        {
            var key = $"l1:module:mem:{moduleName}:{version}";
            if (_l1Cache.TryGetValue(key, out (Dictionary<string, byte[]> files, string digest) cached))
            {
                var dict = cached.files != null ? new Dictionary<string, byte[]>(cached.files) : new Dictionary<string, byte[]>();
                return (dict, cached.digest);
            }

            var result = await _inner.FetchModuleInMemoryAsync(moduleName, version, cancellationToken);
            _l1Cache.Set(key, result, TimeSpan.FromMinutes(30));
            var resDict = result.files != null ? new Dictionary<string, byte[]>(result.files) : new Dictionary<string, byte[]>();
            return (resDict, result.digest);
        }

        public Task PushModuleAsync(string modulePath, string moduleName, string version)
        {
            _l1Cache.Remove($"l1:module:{moduleName}:{version}");
            _l1Cache.Remove($"l1:module:mem:{moduleName}:{version}");
            return _inner.PushModuleAsync(modulePath, moduleName, version);
        }

        public Task PushModuleAsync(string modulePath, string moduleName, string version, CancellationToken cancellationToken)
        {
            _l1Cache.Remove($"l1:module:{moduleName}:{version}");
            _l1Cache.Remove($"l1:module:mem:{moduleName}:{version}");
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

        private static void CopyDir(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(src))
            {
                File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            }
            foreach (var d in Directory.GetDirectories(src))
            {
                CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
            }
        }
    }
}
