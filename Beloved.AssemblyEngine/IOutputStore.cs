using System.IO;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

public interface IOutputStore
{
    Task<string> StoreArtifactAsync(string jobId, Stream artifactStream);
    Task<Stream?> GetArtifactAsync(string jobId);
}
