using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine
{
    public interface IVaultRepository
    {
        Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory);
        Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory, CancellationToken cancellationToken);

        Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory);
        Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory, CancellationToken cancellationToken);

        Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName);
        Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName, CancellationToken cancellationToken);

        Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version);
        Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version, CancellationToken cancellationToken);

        Task PushModuleAsync(string modulePath, string moduleName, string version);
        Task PushModuleAsync(string modulePath, string moduleName, string version, CancellationToken cancellationToken);

        Task<IEnumerable<string>> ListModulesAsync();
        Task<IEnumerable<string>> ListModulesAsync(CancellationToken cancellationToken);

        Task<bool> VerifySignatureAsync(string moduleName, string version);
        Task<bool> VerifySignatureAsync(string moduleName, string version, CancellationToken cancellationToken);
    }
}
