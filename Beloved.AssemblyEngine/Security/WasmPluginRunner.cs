using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wasmtime;

namespace Beloved.AssemblyEngine.Security
{
    /// <summary>
    /// Enterprise WebAssembly (Wasm) isolated sandbox plugin runner.
    /// Executes community Wasm plugins inside a memory-isolated Wasmtime runtime sandbox.
    /// </summary>
    public sealed class WasmPluginRunner : IDisposable
    {
        private readonly Engine _engine;
        private readonly Linker _linker;
        private readonly Store _store;

        public WasmPluginRunner()
        {
            _engine = new Engine();
            _linker = new Linker(_engine);
            _store = new Store(_engine);
        }

        /// <summary>
        /// Runs a WebAssembly module binary within an isolated memory sandbox.
        /// </summary>
        public async Task<string> ExecutePluginAsync(byte[] wasmBytes, string inputData)
        {
            if (wasmBytes == null || wasmBytes.Length == 0)
                throw new ArgumentException("Wasm binary payload cannot be null or empty.");

            return await Task.Run(() =>
            {
                using var module = Module.FromBytes(_engine, "beloved_wasm_plugin", wasmBytes);
                var instance = _linker.Instantiate(_store, module);

                var runFunc = instance.GetFunction("run_plugin");
                if (runFunc == null)
                {
                    // Fall back to WASI _start if run_plugin entry point is absent
                    var startFunc = instance.GetFunction("_start");
                    startFunc?.Invoke();
                    return "Wasm plugin executed via WASI _start";
                }

                var result = runFunc.Invoke();
                return result?.ToString() ?? "Wasm plugin executed successfully (void return)";
            });
        }

        public void Dispose()
        {
            _store.Dispose();
            _linker.Dispose();
            _engine.Dispose();
        }
    }
}
