using System;
using System.IO;
using System.Threading.Tasks;
using Wasmtime;

namespace Beloved.AssemblyEngine;

public class WasmtimeExecutor : IWasmExecutor
{
    public Task<string> ExecuteAsync(byte[] wasmBytes, string functionName, params object[] args)
    {
        try
        {
            using var engine = new Engine();
            using var module = Module.FromBytes(engine, "beloved_module", wasmBytes);
            using var linker = new Linker(engine);
            using var store = new Store(engine);

            // Set up basic WASI environment context
            linker.DefineWasi();
            store.SetWasiConfiguration(new WasiConfiguration());

            var instance = linker.Instantiate(store, module);
            var run = instance.GetFunction(functionName);
            if (run == null)
            {
                throw new InvalidOperationException($"Function {functionName} not found in WASM module.");
            }

            var valueBoxes = new ValueBox[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is string s) valueBoxes[i] = s;
                else if (args[i] is int val) valueBoxes[i] = val;
                else valueBoxes[i] = args[i]?.ToString() ?? string.Empty;
            }

            var result = run.Invoke(valueBoxes);
            return Task.FromResult(result?.ToString() ?? string.Empty);
        }
        catch (Exception ex)
        {
            return Task.FromException<string>(new InvalidOperationException($"WASM execution failed: {ex.Message}", ex));
        }
    }
}
