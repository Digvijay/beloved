using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beloved.Cli;

class Program
{
    private const string ApiBase = "http://127.0.0.1:3000/api";
    private static readonly string GlobalConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beloved", "config.json");
    private static readonly string LocalConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "beloved.json");

    // Single shared HttpClient backed by a pooled SocketsHttpHandler.
    // PooledConnectionLifetime prevents socket exhaustion and DNS staleness.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime    = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    });

    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return;
        }

        var command = args[0].ToLowerInvariant();
        try
        {
            switch (command)
            {
                case "login":
                    if (args.Length < 2) { Console.WriteLine("Usage: beloved login <api-key>"); return; }
                    await LoginAsync(args[1]);
                    break;
                case "init":
                    if (args.Length < 2) { Console.WriteLine("Usage: beloved init <project-name>"); return; }
                    await InitAsync(args[1]);
                    break;
                case "generate":
                    if (args.Length < 2) { Console.WriteLine("Usage: beloved generate \"<intent>\""); return; }
                    await GenerateAsync(args[1]);
                    break;
                case "publish":
                    if (args.Length < 2) { Console.WriteLine("Usage: beloved publish <directory>"); return; }
                    await PublishAsync(args[1]);
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Beloved CLI - Cloud Native Application Assembly Engine");
        Console.WriteLine("Commands:");
        Console.WriteLine("  login <api-key>       Save your tenant API key");
        Console.WriteLine("  init <project-name>   Initialize a new Beloved project in the current directory");
        Console.WriteLine("  generate \"<intent>\"   Map intent to modules, assemble, and download the source code");
        Console.WriteLine("  publish <directory>   Publish a local component module to the OCI Vault");
    }

    private static async Task LoginAsync(string apiKey)
    {
        // Save to ~/.beloved/config.json
        var dir = Path.GetDirectoryName(GlobalConfigPath)!;
        Directory.CreateDirectory(dir);

        var config = new { ApiKey = apiKey };
        await File.WriteAllTextAsync(GlobalConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Successfully logged in. API Key saved.");
        Console.ResetColor();
    }

    private static string GetApiKey()
    {
        if (!File.Exists(GlobalConfigPath))
            throw new Exception("You must run 'beloved login <api-key>' first.");

        var json = File.ReadAllText(GlobalConfigPath);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("ApiKey").GetString()!;
    }

    private static string GetProjectId()
    {
        if (!File.Exists(LocalConfigPath))
            throw new Exception("You must run 'beloved init <project-name>' in this directory first.");

        var json = File.ReadAllText(LocalConfigPath);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("ProjectId").GetString()!;
    }

    private static async Task InitAsync(string projectName)
    {
        var apiKey = GetApiKey();
        Http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        Console.WriteLine($"Provisioning project '{projectName}'...");
        var response = await Http.PostAsJsonAsync($"{ApiBase}/projects", new { name = projectName });
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var projectId = doc.RootElement.GetProperty("id").GetString()!;

        // Save local project state
        var localConfig = new { ProjectId = projectId, ProjectName = projectName };
        await File.WriteAllTextAsync(LocalConfigPath, JsonSerializer.Serialize(localConfig, new JsonSerializerOptions { WriteIndented = true }));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Project '{projectName}' initialized. ProjectId: {projectId}");
        Console.WriteLine("You can now run 'beloved generate \"<intent>\"'.");
        Console.ResetColor();
    }

    private static async Task GenerateAsync(string intent)
    {
        var apiKey = GetApiKey();
        var projectId = GetProjectId();
        Http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        Console.WriteLine("1. Mapping Intent to Blueprint...");
        var mapResponse = await Http.PostAsJsonAsync($"{ApiBase}/intent", new { prompt = intent });
        mapResponse.EnsureSuccessStatusCode();
        var blueprintJson = await mapResponse.Content.ReadAsStringAsync();

        Console.WriteLine("2. Queueing Assembly Job...");
        var assemblePayload = new
        {
            projectId = projectId,
            blueprint = JsonSerializer.Deserialize<JsonElement>(blueprintJson)
        };
        var assembleResponse = await Http.PostAsJsonAsync($"{ApiBase}/assemble", assemblePayload);
        assembleResponse.EnsureSuccessStatusCode();
        
        var assembleResult = await assembleResponse.Content.ReadAsStringAsync();
        using var assembleDoc = JsonDocument.Parse(assembleResult);
        var jobId = assembleDoc.RootElement.GetProperty("jobId").GetString()!;
        Console.WriteLine($"   Job ID: {jobId}");

        Console.WriteLine("3. Waiting for compilation artifacts...");
        // Poll for artifact download
        var maxRetries = 30;
        for (int i = 0; i < maxRetries; i++)
        {
            await Task.Delay(2000);
            var artifactResponse = await Http.GetAsync($"{ApiBase}/artifacts/{jobId}");
            if (artifactResponse.IsSuccessStatusCode)
            {
                var zipPath = Path.Combine(Directory.GetCurrentDirectory(), $"{projectId}.zip");
                using var fs = new FileStream(zipPath, FileMode.Create);
                await artifactResponse.Content.CopyToAsync(fs);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"SUCCESS: Application assembled and downloaded to {zipPath}");
                Console.ResetColor();
                return;
            }
        }
        throw new Exception("Assembly timed out.");
    }

    private static async Task PublishAsync(string directory)
    {
        var apiKey = GetApiKey();
        Http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath)) throw new Exception($"Directory not found: {fullPath}");
        
        var manifestPath = Path.Combine(fullPath, "manifest.json");
        if (!File.Exists(manifestPath)) throw new Exception("manifest.json is required in the module root.");

        Console.WriteLine($"Zipping module from {fullPath}...");
        var tempZip = Path.GetTempFileName() + ".zip";
        System.IO.Compression.ZipFile.CreateFromDirectory(fullPath, tempZip);

        try
        {
            Console.WriteLine("Uploading to Control Plane...");
            using var form = new MultipartFormDataContent();
            using var fileStream = new FileStream(tempZip, FileMode.Open, FileAccess.Read);
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            form.Add(streamContent, "file", "module.zip");

            var response = await Http.PostAsync($"{ApiBase}/modules/publish", form);
            
            if (response.IsSuccessStatusCode)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: " + await response.Content.ReadAsStringAsync());
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED: " + await response.Content.ReadAsStringAsync());
                Console.ResetColor();
            }
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }
}
