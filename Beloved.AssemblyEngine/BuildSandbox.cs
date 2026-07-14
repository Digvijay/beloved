using System.IO;
using System.Text;

namespace Beloved.AssemblyEngine;

public class BuildSandbox
{
    public static bool VerifyBuild(string appPath)
    {
        if (string.IsNullOrWhiteSpace(appPath) || !Directory.Exists(appPath))
        {
            return false;
        }

        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build",
                WorkingDirectory = appPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = System.Diagnostics.Process.Start(info);
            if (process == null) return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
