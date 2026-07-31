using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CommandLine;
using Spectre.Console;
using Beloved.AssemblyEngine;
using Beloved.AssemblyEngine.Security;

namespace Beloved.Cli;

[Verb("login", HelpText = "Save your encrypted tenant API key.")]
public class LoginOptions
{
    [Value(0, MetaName = "api-key", Required = true, HelpText = "Tenant API Key")]
    public string ApiKey { get; set; } = string.Empty;
}

[Verb("logout", HelpText = "Purge stored encrypted API key credentials from disk.")]
public class LogoutOptions { }

[Verb("init", HelpText = "Initialize a new Beloved project in the current directory.")]
public class InitOptions
{
    [Value(0, MetaName = "project-name", Required = true, HelpText = "Project Name")]
    public string ProjectName { get; set; } = string.Empty;
}

[Verb("generate", HelpText = "Map intent to modules, assemble, and download the source code.")]
public class GenerateOptions
{
    [Value(0, MetaName = "intent", Required = true, HelpText = "Natural language application intent")]
    public string Intent { get; set; } = string.Empty;
}

[Verb("publish", HelpText = "Publish & verify a component module to the OCI Vault.")]
public class PublishOptions
{
    [Value(0, MetaName = "directory", Required = true, HelpText = "Module directory path")]
    public string DirectoryPath { get; set; } = string.Empty;
}

[Verb("completion", HelpText = "Generate shell auto-completion scripts (zsh, bash).")]
public class CompletionOptions
{
    [Value(0, MetaName = "shell", Required = true, HelpText = "Shell type: zsh | bash")]
    public string Shell { get; set; } = "zsh";
}

[Verb("module", HelpText = "Manage Beloved component modules (init, catalog, push, search, unpublish).")]
public class ModuleOptions
{
    [Value(0, MetaName = "subcommand", Required = true, HelpText = "Subcommand: init | catalog | push | search | unpublish")]
    public string SubCommand { get; set; } = string.Empty;

    [Value(1, MetaName = "arg1", Required = false, HelpText = "First argument (module name or target path)")]
    public string? Arg1 { get; set; }

    [Value(2, MetaName = "arg2", Required = false, HelpText = "Second argument (version or registry URL)")]
    public string? Arg2 { get; set; }
}

class Program
{
    private static readonly string ApiBase = Environment.GetEnvironmentVariable("BELOVED_API_URL") ?? "http://localhost:3000/api";
    private static readonly string GlobalConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beloved", "config.enc");
    private static readonly string LocalConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "beloved.json");

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    });

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            RenderHeader();
        }

        var parserResult = Parser.Default.ParseArguments<
            LoginOptions,
            LogoutOptions,
            InitOptions,
            GenerateOptions,
            PublishOptions,
            CompletionOptions,
            ModuleOptions
        >(args);

        return await parserResult.MapResult(
            (LoginOptions opts) => { Login(opts.ApiKey); return Task.FromResult(0); },
            (LogoutOptions opts) => { Logout(); return Task.FromResult(0); },
            async (InitOptions opts) => { await InitAsync(opts.ProjectName); return 0; },
            async (GenerateOptions opts) => { await GenerateAsync(opts.Intent); return 0; },
            async (PublishOptions opts) => { await PublishAsync(opts.DirectoryPath); return 0; },
            (CompletionOptions opts) => { GenerateCompletion(opts.Shell); return Task.FromResult(0); },
            async (ModuleOptions opts) => { await HandleModuleSubcommandAsync(opts); return 0; },
            errs => Task.FromResult(1)
        );
    }

    private static void RenderHeader()
    {
        AnsiConsole.Write(new FigletText("BELOVED").Color(Color.DeepSkyBlue1));
        AnsiConsole.MarkupLine("[bold grey]Cloud Native Application Assembly Engine v0.1.0[/]\n");
    }

    private static void Login(string apiKey)
    {
        SecureConfigStore.SaveApiKey(GlobalConfigPath, apiKey);
        AnsiConsole.MarkupLine("[bold green]✓ Successfully logged in.[/] Encrypted API Key stored safely at ~/.beloved/config.enc");
    }

    private static void Logout()
    {
        SecureConfigStore.ClearConfig(GlobalConfigPath);
        AnsiConsole.MarkupLine("[bold yellow]✓ Successfully logged out.[/] Stored credentials securely purged from disk.");
    }

    private static string GetApiKey()
    {
        return SecureConfigStore.ReadApiKey(GlobalConfigPath);
    }

    private static string GetProjectId()
    {
        if (!File.Exists(LocalConfigPath))
            throw new InvalidOperationException("You must run 'beloved init <project-name>' in this directory first.");

        var json = File.ReadAllText(LocalConfigPath);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("ProjectId").GetString()!;
    }

    private static async Task InitAsync(string projectName)
    {
        await AnsiConsole.Status().StartAsync($"Provisioning project '{projectName}'...", async ctx =>
        {
            var apiKey = GetApiKey();
            Http.DefaultRequestHeaders.Remove("X-Api-Key");
            Http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

            var response = await Http.PostAsJsonAsync($"{ApiBase}/projects", new { name = projectName });
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var projectId = doc.RootElement.GetProperty("id").GetString()!;

            var localConfig = new { ProjectId = projectId, Name = projectName };
            await File.WriteAllTextAsync(LocalConfigPath, JsonSerializer.Serialize(localConfig, new JsonSerializerOptions { WriteIndented = true }));

            AnsiConsole.MarkupLine($"[bold green]✓ Project '{projectName}' initialized successfully![/] (ID: [cyan]{projectId}[/])");
        });
    }

    private static async Task GenerateAsync(string intent)
    {
        await AnsiConsole.Status().StartAsync($"Mapping intent and assembling app...", async ctx =>
        {
            var apiKey = GetApiKey();
            var projectId = GetProjectId();

            Http.DefaultRequestHeaders.Remove("X-Api-Key");
            Http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

            ctx.Status($"Submitting intent: \"{intent}\"...");
            var response = await Http.PostAsJsonAsync($"{ApiBase}/projects/{projectId}/assemble", new { intent });
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var jobId = doc.RootElement.GetProperty("jobId").GetString()!;

            ctx.Status($"Assembly job [cyan]{jobId}[/] queued. Compiling & downloading output zip...");
            var artifactBytes = await Http.GetByteArrayAsync($"{ApiBase}/projects/jobs/{jobId}/download");

            var outputZip = Path.Combine(Directory.GetCurrentDirectory(), $"{projectId}_output.zip");
            await File.WriteAllBytesAsync(outputZip, artifactBytes);

            AnsiConsole.MarkupLine($"[bold green]✓ SUCCESS: Application assembly complete![/] Output saved to: [cyan]{outputZip}[/]");
        });
    }

    private static async Task PublishAsync(string directoryPath)
    {
        await AnsiConsole.Status().StartAsync($"Verifying and publishing module from '{directoryPath}'...", async ctx =>
        {
            var apiKey = GetApiKey();
            Http.DefaultRequestHeaders.Remove("X-Api-Key");
            Http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

            var absPath = Path.GetFullPath(directoryPath);
            if (!Directory.Exists(absPath))
            {
                throw new DirectoryNotFoundException($"Directory '{absPath}' does not exist.");
            }

            var zipPath = Path.Combine(Path.GetTempPath(), $"module_{Guid.NewGuid():N}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            System.IO.Compression.ZipFile.CreateFromDirectory(absPath, zipPath);

            using var content = new MultipartFormDataContent();
            using var fs = File.OpenRead(zipPath);
            content.Add(new StreamContent(fs), "file", Path.GetFileName(zipPath));

            ctx.Status("Uploading & executing Roslyn static AST security analysis...");
            var response = await Http.PostAsync($"{ApiBase}/modules/submit", content);
            
            if (File.Exists(zipPath)) File.Delete(zipPath);

            var respString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]✗ FAILED: Security verification rejected.[/]\n[grey]{respString}[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[bold green]✓ SUCCESS: Component module verified and published to OCI Vault![/]\n[grey]{respString}[/]");
        });
    }

    private static void GenerateCompletion(string shell)
    {
        if (shell.ToLowerInvariant() == "zsh")
        {
            AnsiConsole.WriteLine("#compdef beloved\n\n_beloved() {\n    local -a commands\n    commands=(\n        'login:Save encrypted API key'\n        'logout:Purge credentials'\n        'init:Initialize project'\n        'generate:Assemble application from intent'\n        'publish:Publish OCI module'\n        'module:Manage component modules'\n    )\n    _describe -t commands 'beloved commands' commands\n}\n_beloved \"$@\"");
        }
        else
        {
            AnsiConsole.WriteLine("complete -W \"login logout init generate publish module completion\" beloved");
        }
    }

    private static async Task HandleModuleSubcommandAsync(ModuleOptions opts)
    {
        var subCmd = opts.SubCommand.ToLowerInvariant();
        switch (subCmd)
        {
            case "init":
                if (string.IsNullOrWhiteSpace(opts.Arg1))
                    throw new ArgumentException("Module name is required. Usage: beloved module init <name>");
                await InitModuleAsync(opts.Arg1);
                break;
            case "catalog":
                await CatalogModulesAsync();
                break;
            case "push":
                if (string.IsNullOrWhiteSpace(opts.Arg1))
                    throw new ArgumentException("Module directory is required. Usage: beloved module push <dir> [registryUrl]");
                var regUrl = !string.IsNullOrWhiteSpace(opts.Arg2) ? opts.Arg2 : "http://localhost:5001";
                await PushModuleToOciAsync(opts.Arg1, regUrl);
                break;
            case "search":
                if (string.IsNullOrWhiteSpace(opts.Arg1))
                    throw new ArgumentException("Search term is required. Usage: beloved module search <query>");
                await SearchModulesAsync(opts.Arg1);
                break;
            case "unpublish":
                if (string.IsNullOrWhiteSpace(opts.Arg1) || string.IsNullOrWhiteSpace(opts.Arg2))
                    throw new ArgumentException("Name and version are required. Usage: beloved module unpublish <name> <version>");
                await UnpublishModuleAsync(opts.Arg1, opts.Arg2);
                break;
            default:
                AnsiConsole.MarkupLine($"[bold red]Unknown module subcommand: '{opts.SubCommand}'.[/] Options: init | catalog | push | search | unpublish");
                break;
        }
    }

    private static async Task InitModuleAsync(string moduleName)
    {
        var targetDir = Path.Combine(Directory.GetCurrentDirectory(), moduleName);
        if (Directory.Exists(targetDir))
        {
            throw new InvalidOperationException($"Directory '{targetDir}' already exists.");
        }

        Directory.CreateDirectory(targetDir);

        var manifestJson = JsonSerializer.Serialize(new
        {
            Name = moduleName,
            Version = "1.0.0",
            Description = $"{moduleName} community component module",
            Category = "General"
        }, new JsonSerializerOptions { WriteIndented = true });

        var sampleCode = $@"namespace Beloved.Modules.{moduleName};

public class {moduleName}Module
{{
    public void Initialize()
    {{
        // Component initialization logic
    }}
}}";

        var readmeMd = $"# {moduleName}\n\nBeloved component module boilerplate.";

        await File.WriteAllTextAsync(Path.Combine(targetDir, "manifest.json"), manifestJson);
        await File.WriteAllTextAsync(Path.Combine(targetDir, $"{moduleName}Module.cs"), sampleCode);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "README.md"), readmeMd);

        AnsiConsole.MarkupLine($"[bold green]✓ SUCCESS: Module boilerplate '{moduleName}' created at[/] [cyan]{targetDir}[/]");
    }

    private static async Task CatalogModulesAsync()
    {
        var response = await Http.GetAsync($"{ApiBase}/modules/catalog");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold cyan]NAME[/]");
        table.AddColumn("[bold cyan]VERSION[/]");
        table.AddColumn("[bold cyan]CATEGORY[/]");
        table.AddColumn("[bold cyan]VERIFIED[/]");
        table.AddColumn("[bold cyan]SOURCE[/]");
        table.AddColumn("[bold cyan]DESCRIPTION[/]");

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var name = el.GetProperty("name").GetString() ?? "";
            var version = el.GetProperty("version").GetString() ?? "1.0.0";
            var category = el.GetProperty("category").GetString() ?? "General";
            var isVerified = el.GetProperty("isVerified").GetBoolean() ? "[green]✓ YES[/]" : "[yellow]PENDING[/]";
            var source = el.GetProperty("source").GetString() ?? "Community";
            var desc = el.GetProperty("description").GetString() ?? "";

            table.AddRow(name, version, category, isVerified, source, desc);
        }

        AnsiConsole.Write(table);
    }

    private static async Task PushModuleToOciAsync(string directoryPath, string registryUrl)
    {
        var absPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(absPath))
        {
            throw new DirectoryNotFoundException($"Directory '{absPath}' does not exist.");
        }

        var manifestFile = Path.Combine(absPath, "manifest.json");
        if (!File.Exists(manifestFile))
        {
            throw new FileNotFoundException($"Module manifest file missing at '{manifestFile}'.");
        }

        var manifestJson = await File.ReadAllTextAsync(manifestFile);
        using var doc = JsonDocument.Parse(manifestJson);
        var name = doc.RootElement.GetProperty("Name").GetString() ?? "UnknownModule";
        var version = doc.RootElement.TryGetProperty("Version", out var vEl) ? vEl.GetString() ?? "1.0.0" : "1.0.0";

        await AnsiConsole.Status().StartAsync($"Pushing OCI artifact '{name}:{version}' to registry '{registryUrl}'...", async ctx =>
        {
            var ociClient = new OciRegistryClient(Http);
            var manifestDigest = await ociClient.PushModuleDirectoryAsync(registryUrl, name, version, absPath);

            AnsiConsole.MarkupLine($"[bold green]✓ SUCCESS: Published OCI layer to {registryUrl}/modules/{name.ToLower()}:{version}[/]");
            AnsiConsole.MarkupLine($"[grey]Digest: {manifestDigest}[/]");
        });
    }

    private static async Task SearchModulesAsync(string query)
    {
        var response = await Http.GetAsync($"{ApiBase}/modules/search?query={Uri.EscapeDataString(query)}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var total = doc.RootElement.GetProperty("totalCount").GetInt32();
        var results = doc.RootElement.GetProperty("results");

        var table = new Table();
        table.Title($"[bold cyan]Found {total} module(s) matching '{query}'[/]");
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold cyan]NAME[/]");
        table.AddColumn("[bold cyan]VERSION[/]");
        table.AddColumn("[bold cyan]CATEGORY[/]");
        table.AddColumn("[bold cyan]VERIFIED[/]");
        table.AddColumn("[bold cyan]DOWNLOADS[/]");
        table.AddColumn("[bold cyan]DESCRIPTION[/]");

        foreach (var el in results.EnumerateArray())
        {
            var name = el.GetProperty("name").GetString() ?? "";
            var version = el.GetProperty("version").GetString() ?? "1.0.0";
            var category = el.GetProperty("category").GetString() ?? "General";
            var isVerified = el.GetProperty("isVerified").GetBoolean() ? "[green]✓ YES[/]" : "[grey]NO[/]";
            var downloads = el.GetProperty("downloadsCount").GetInt32().ToString();
            var desc = el.GetProperty("description").GetString() ?? "";

            table.AddRow(name, version, category, isVerified, downloads, desc);
        }

        AnsiConsole.Write(table);
    }

    private static async Task UnpublishModuleAsync(string name, string version)
    {
        await AnsiConsole.Status().StartAsync($"Unpublishing module '{name}:{version}' (NuGet immutability policy)...", async ctx =>
        {
            var response = await Http.PostAsync($"{ApiBase}/modules/unpublish/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}", null);
            
            if (!response.IsSuccessStatusCode)
            {
                var errJson = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[bold red]✗ FAILED: Could not unpublish module '{name}:{version}'.[/]\n[grey]{errJson}[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[bold yellow]✓ SUCCESS: Module '{name}:{version}' soft-deleted and unpublished from vault.[/]");
            AnsiConsole.MarkupLine("[grey]Re-publishing this exact version is permanently barred per immutability policy.[/]");
        });
    }
}
