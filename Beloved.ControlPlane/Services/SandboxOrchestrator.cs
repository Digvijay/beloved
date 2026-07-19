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

// Generate docker-compose.yml
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
      - '3001:5173'
    command: npm run dev -- --host 0.0.0.0
    depends_on:
      - backend
";
        await File.WriteAllTextAsync(Path.Combine(_activeTempWorkspace, "docker-compose.yml"), composeContent);

        // Run docker compose up
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "compose up -d --build",
            WorkingDirectory = _activeTempWorkspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        var customDockerConfig = Path.Combine(_activeTempWorkspace, ".docker");
        Directory.CreateDirectory(customDockerConfig);
        startInfo.EnvironmentVariables["DOCKER_CONFIG"] = customDockerConfig;

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

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "compose down -v",
            WorkingDirectory = _activeTempWorkspace,
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
        _activeJobId = null;

        return true;
    }
}
