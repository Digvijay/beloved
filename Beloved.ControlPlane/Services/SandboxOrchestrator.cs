using Beloved.AssemblyEngine;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services;

public class SandboxOrchestrator
{
    private readonly IOutputStore _outputStore;
    private string? _activeTempWorkspace;
    private string? _activeAppRoot;
    private string? _activeJobId;

    public SandboxOrchestrator(IOutputStore outputStore)
    {
        _outputStore = outputStore;
    }

    public virtual async Task<(bool success, string error, string url)> StartSandboxAsync(string jobId)
    {
        if (_activeTempWorkspace != null)
        {
            await StopSandboxAsync();
        }

        var artifactStream = await _outputStore.GetArtifactAsync(jobId);
        if (artifactStream == null)
        {
            return (false, "Artifact not found for JobId: " + jobId, "");
        }

        _activeJobId = jobId;
        _activeTempWorkspace = Path.Combine(Path.GetTempPath(), "beloved_sandbox_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_activeTempWorkspace);

        // Extract artifact
        var zipPath = Path.Combine(_activeTempWorkspace, "app.zip");
        using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        {
            await artifactStream.CopyToAsync(fs);
        }
        
        // Ensure stream is closed before extraction
        artifactStream.Close();
        
        ZipFile.ExtractToDirectory(zipPath, _activeTempWorkspace);
        File.Delete(zipPath); // clean up the zip

        // The artifact extracts into a named subdirectory (e.g. SaaSApp/).
        // Detect it so volumes and working_dir resolve correctly.
        var appRoot = Directory.GetDirectories(_activeTempWorkspace).FirstOrDefault()
                      ?? _activeTempWorkspace;
        _activeAppRoot = appRoot;

        // Generate docker-compose.yml inside the app root
        var composeContent = $@"
services:
  backend:
    image: mcr.microsoft.com/dotnet/sdk:9.0
    working_dir: /app/backend
    volumes:
      - .:/app
    ports:
      - '5002:8080'
    command: dotnet run
  frontend:
    image: node:20
    working_dir: /app/frontend
    volumes:
      - .:/app
    ports:
      - '3001:3000'
    command: sh -c 'npm install && npm run dev -- --host 0.0.0.0'
    depends_on:
      - backend
";
        await File.WriteAllTextAsync(Path.Combine(appRoot, "docker-compose.yml"), composeContent);

        // Run docker compose up from the app root
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "compose up -d",
            WorkingDirectory = appRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var err = await process.StandardError.ReadToEndAsync();
                return (false, "Docker compose failed: " + err, "");
            }
        }

        // Return the frontend URL (port 3001)
        return (true, "", "http://localhost:3001");
    }

    public virtual async Task<bool> StopSandboxAsync()
    {
        if (string.IsNullOrEmpty(_activeTempWorkspace)) return true;

        var composeDir = _activeAppRoot ?? _activeTempWorkspace;
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "compose down -v",
            WorkingDirectory = composeDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        startInfo.EnvironmentVariables["DOCKER_CONFIG"] = Path.Combine(_activeTempWorkspace, ".docker");

        var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }

        if (Directory.Exists(_activeTempWorkspace))
        {
            Directory.Delete(_activeTempWorkspace, true);
        }

        _activeTempWorkspace = null;
        _activeAppRoot = null;
        _activeJobId = null;

        return true;
    }
}
