using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine.Drivers
{
    public record OutputDriverContext(
        string ApplicationName,
        string Description,
        Dictionary<string, byte[]> SharedAssets,
        Dictionary<string, string> EnvironmentVariables
    );

    /// <summary>
    /// Core abstraction for multi-target output generation.
    /// Allows Beloved AssemblyEngine to generate Web Apps (React/Vite), Native Desktop/Mobile (Tauri),
    /// and Backend Microservices (ASP.NET Core Minimal API) from unified AST intent models.
    /// </summary>
    public interface IOutputTargetDriver
    {
        string TargetName { get; }
        string SupportedExtension { get; }
        Task<Dictionary<string, byte[]>> GenerateOutputFilesAsync(
            OutputDriverContext context, CancellationToken cancellationToken = default);
    }
}
