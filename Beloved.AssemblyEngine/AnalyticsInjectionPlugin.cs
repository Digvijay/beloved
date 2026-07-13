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

    public Task ExecuteInMemoryAsync(System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> workspace, Blueprint blueprint, Action<string>? onLog = null)
    {
        // Standardize key check to match relative path
        var key = "frontend/src/index.html";
        if (!workspace.ContainsKey(key))
        {
            key = "frontend/index.html"; // fallback
        }

        if (!workspace.ContainsKey(key))
        {
            onLog?.Invoke($"[Plugin: {Name}] No index.html found in memory. Skipping injection.");
            return Task.CompletedTask;
        }

        onLog?.Invoke($"[Plugin: {Name}] Injecting analytics and compilation metadata into index.html in-memory...");

        var contentBytes = workspace[key];
        var content = System.Text.Encoding.UTF8.GetString(contentBytes);

        var injectContent = "\n    <!-- Compiled by Beloved Assembly Engine (In-Memory) -->\n    <script>console.log('Beloved Telemetry initialized for " + blueprint.AppName + "');</script>\n  </body>";
        content = content.Replace("</body>", injectContent);

        workspace[key] = System.Text.Encoding.UTF8.GetBytes(content);
        onLog?.Invoke($"[Plugin: {Name}] Successfully injected telemetry metadata in-memory.");
        return Task.CompletedTask;
    }
}
