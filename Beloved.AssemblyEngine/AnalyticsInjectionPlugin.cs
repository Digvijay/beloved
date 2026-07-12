using System;
using System.IO;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

/// <summary>
/// A default assembly plugin that injects assembly metadata and analytics scripts
/// into index.html if a frontend exists.
/// </summary>
public class AnalyticsInjectionPlugin : IAssemblyPlugin
{
    public string Name => "AnalyticsInjection";

    public async Task ExecuteAsync(string appPath, Blueprint blueprint, Action<string>? onLog = null)
    {
        var htmlPath = Path.Combine(appPath, "frontend", "index.html");
        if (!File.Exists(htmlPath))
        {
            onLog?.Invoke($"[Plugin: {Name}] No index.html found. Skipping injection.");
            return;
        }

        onLog?.Invoke($"[Plugin: {Name}] Injecting analytics and compilation metadata into index.html...");
        
        var content = await File.ReadAllTextAsync(htmlPath);
        
        // Inject a custom HTML comment and analytics script hook before the closing body tag
        var injectContent = "\n    <!-- Compiled by Beloved Assembly Engine -->\n    <script>console.log('Beloved Telemetry initialized for " + blueprint.AppName + "');</script>\n  </body>";
        
        content = content.Replace("</body>", injectContent);
        
        await File.WriteAllTextAsync(htmlPath, content);
        onLog?.Invoke($"[Plugin: {Name}] Successfully injected telemetry metadata.");
    }
}
