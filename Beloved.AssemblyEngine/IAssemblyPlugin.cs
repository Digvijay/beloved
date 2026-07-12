using System;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

/// <summary>
/// Hook interface allowing custom processing steps (e.g., linting, minification, injecting scripts)
/// to run during the application assembly pipeline.
/// </summary>
public interface IAssemblyPlugin
{
    string Name { get; }
    Task ExecuteAsync(string appPath, Blueprint blueprint, Action<string>? onLog = null);
}
