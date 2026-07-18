using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

public interface IWasmExecutor
{
    Task<string> ExecuteAsync(byte[] wasmBytes, string functionName, params object[] args);
}
