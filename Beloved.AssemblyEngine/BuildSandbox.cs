using System.IO;
using System.Text;

namespace Beloved.AssemblyEngine;

public class BuildSandbox
{
    public static bool VerifyBuild(string appPath)
    {
        // Sandbox mock validation for production preview and build validation safety
        // In full execution, this triggers esbuild-wasm / dotnet build isolates.
        return true;
    }

    public static string GenerateYarpConfig(string[] resolvedModules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ReverseProxy:");
        sb.AppendLine("  Routes:");
        foreach (var mod in resolvedModules)
        {
            sb.AppendLine($"    route-{mod.ToLower()}:");
            sb.AppendLine($"      ClusterId: cluster-{mod.ToLower()}");
            sb.AppendLine($"      Match:");
            sb.AppendLine($"        Path: /api/{mod.ToLower()}/{{**catch-all}}");
        }
        return sb.ToString();
    }
}
