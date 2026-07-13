using System.Collections.Generic;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine
{
    public interface IVaultRepository
    {
        Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory);
        Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory);
        Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName);
        Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version);
        Task PushModuleAsync(string modulePath, string moduleName, string version);
        Task<IEnumerable<string>> ListModulesAsync();
    }
}
