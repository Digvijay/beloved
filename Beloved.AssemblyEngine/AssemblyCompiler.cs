using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Beloved.AssemblyEngine;

public class Blueprint
{
    public string AppName { get; set; } = "BelovedApp";
    public List<string> Modules { get; set; } = new();
    public string Database { get; set; } = "SQLite"; // SQLite, PostgreSQL, SQLServer
    public string AuthStrategy { get; set; } = "None"; // None, JWT
    public string Target { get; set; } = "WebAndApi"; // WebAndApi, ApiOnly, Mobile
}

public class ModuleManifest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FrontendConfig Frontend { get; set; } = new();
    public BackendConfig Backend { get; set; } = new();
}

public class FrontendConfig
{
    public string Nav { get; set; } = string.Empty;
    public string Views { get; set; } = string.Empty;
    public string Imports { get; set; } = string.Empty;
}

public class BackendConfig
{
    public List<string> Controllers { get; set; } = new();
    public string DbSets { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Blueprint))]
[JsonSerializable(typeof(ModuleManifest))]
[JsonSerializable(typeof(Sbom))]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class AssemblyJsonContext : JsonSerializerContext
{
}

public class AssemblyResult
{
    public bool Success { get; set; }
    public string SbomJson { get; set; } = string.Empty;
}

public class Sbom
{
    public string ProjectName { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public DateTime AssembledAt { get; set; } = DateTime.UtcNow;
    public List<SbomComponent> Components { get; set; } = new();
}

public class SbomComponent
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Digest { get; set; } = string.Empty;
    public string Source { get; set; } = "localhost:5001";
}

public class AssemblyCompiler
{
    private readonly IVaultRepository _vaultRepository;
    private readonly IEnumerable<IAssemblyPlugin> _plugins;
    private readonly ILlmProvider? _llmProvider;

    private static readonly System.Diagnostics.ActivitySource CompilerActivitySource = new System.Diagnostics.ActivitySource("Beloved.AssemblyEngine");

    public AssemblyCompiler(IVaultRepository vaultRepository, IEnumerable<IAssemblyPlugin> plugins, ILlmProvider? llmProvider = null)
    {
        _vaultRepository = vaultRepository;
        _plugins = plugins;
        _llmProvider = llmProvider;
    }

    public async Task<AssemblyResult> AssembleAsync(string blueprintJson, string jobId, IOutputStore outputStore, Action<string>? onLog = null)
    {
        using var activity = CompilerActivitySource.StartActivity("AssembleApp");
        activity?.SetTag("job.id", jobId);

        Blueprint? blueprint;
        try
        {
            blueprint = JsonSerializer.Deserialize(blueprintJson, AssemblyJsonContext.Default.Blueprint);
            if (blueprint == null) return new AssemblyResult { Success = false };
        }
        catch (JsonException)
        {
            return new AssemblyResult { Success = false };
        }

        onLog?.Invoke($"[In-Memory] Starting assembly for application: {blueprint.AppName}");

        var cleanAppName = blueprint.AppName.Replace(" ", "");
        var isApiOnly = blueprint.Target.Equals("ApiOnly", StringComparison.OrdinalIgnoreCase);

        var sbom = new Sbom { ProjectName = cleanAppName, JobId = jobId };

        // Concurrent in-memory workspace: maps relative file path (e.g., "backend/Program.cs") to byte array content
        var workspace = new ConcurrentDictionary<string, byte[]>();

        // 1. Fetch base templates concurrently in-memory
        onLog?.Invoke("[In-Memory] Fetching base templates from OCI Vault in parallel...");
        var reactTask = isApiOnly 
            ? Task.FromResult<(Dictionary<string, byte[]> files, string digest)>((new(), "")) 
            : _vaultRepository.FetchTemplateInMemoryAsync("react-frontend");
        var dotnetTask = _vaultRepository.FetchTemplateInMemoryAsync("dotnet-backend");

        await Task.WhenAll(reactTask, dotnetTask);

        var (reactFiles, reactDigest) = await reactTask;
        var (dotnetFiles, dotnetDigest) = await dotnetTask;

        if (!isApiOnly)
        {
            foreach (var file in reactFiles)
            {
                // Prefix with frontend/
                var key = "frontend/" + file.Key.TrimStart('/');
                workspace[key] = file.Value;
            }
            sbom.Components.Add(new SbomComponent { Name = "templates/react-frontend", Version = "latest", Digest = reactDigest });
        }

        foreach (var file in dotnetFiles)
        {
            // Prefix with backend/
            var key = "backend/" + file.Key.TrimStart('/');
            workspace[key] = file.Value;
        }
        sbom.Components.Add(new SbomComponent { Name = "templates/dotnet-backend", Version = "latest", Digest = dotnetDigest });

        // Thread-safe accumulators for stitching
        var navInjections    = new ConcurrentBag<string>();
        var viewInjections   = new ConcurrentBag<string>();
        var importInjections = new ConcurrentBag<string>();
        var dbSetInjections  = new ConcurrentBag<string>();
        var sbomEntries      = new ConcurrentBag<SbomComponent>();

        try
        {
            // 2. Fetch and process ALL modules concurrently in-memory
            onLog?.Invoke($"[In-Memory] Fetching {blueprint.Modules.Count} module(s) from Vault in parallel...");
            await Task.WhenAll(blueprint.Modules.Select(modName =>
                ProcessModuleInMemoryAsync(
                    modName, isApiOnly, workspace,
                    navInjections, viewInjections, importInjections, dbSetInjections, sbomEntries,
                    onLog)));

            foreach (var entry in sbomEntries) sbom.Components.Add(entry);
            
            onLog?.Invoke("[In-Memory] Stitching module definitions into core engine...");
            
            // 3. Stitch Backend Program.cs
            var programPath = "backend/Program.cs";
            if (workspace.TryGetValue(programPath, out var programBytes))
            {
                var programContent = Encoding.UTF8.GetString(programBytes);
                var sb = new StringBuilder(programContent);

                // Inject Database setup
                var dbSetup = "builder.Services.AddDbContext<AppDbContext>(options =>\n    options.UseSqlite(\"Data Source=app.db\"));";
                if (blueprint.Database.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
                {
                    dbSetup = "builder.Services.AddDbContext<AppDbContext>(options =>\n    options.UseNpgsql(builder.Configuration.GetConnectionString(\"DefaultConnection\") ?? \"Host=localhost;Database=appdb;Username=postgres;Password=postgres\"));";
                }
                else if (blueprint.Database.Equals("SQLServer", StringComparison.OrdinalIgnoreCase))
                {
                    dbSetup = "builder.Services.AddDbContext<AppDbContext>(options =>\n    options.UseSqlServer(builder.Configuration.GetConnectionString(\"DefaultConnection\") ?? \"Server=localhost;Database=appdb;Trusted_Connection=True;TrustServerCertificate=True\"));";
                }

                var dbStart = programContent.IndexOf("// DATABASE_INJECTION_START");
                var dbEnd = programContent.IndexOf("// DATABASE_INJECTION_END");
                if (dbStart >= 0 && dbEnd > dbStart)
                {
                    var oldDbBlock = programContent.Substring(dbStart, dbEnd - dbStart + "// DATABASE_INJECTION_END".Length);
                    sb.Replace(oldDbBlock, dbSetup);
                }

                sb.Replace(
                    "// DBSETS_PLACEHOLDER",
                    string.Join("\n    ", dbSetInjections.ToList())
                );
                workspace[programPath] = Encoding.UTF8.GetBytes(sb.ToString());

                // Update csproj with database package references if needed
                var csprojPath = "backend/dotnet-backend.csproj";
                if (workspace.TryGetValue(csprojPath, out var csprojBytes))
                {
                    var csprojContent = Encoding.UTF8.GetString(csprojBytes);
                    if (blueprint.Database.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
                    {
                        csprojContent = csprojContent.Replace(
                            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"9.0.0\" />",
                            "<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" Version=\"9.0.0\" />"
                        );
                    }
                    else if (blueprint.Database.Equals("SQLServer", StringComparison.OrdinalIgnoreCase))
                    {
                        csprojContent = csprojContent.Replace(
                            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"9.0.0\" />",
                            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" Version=\"9.0.0\" />"
                        );
                    }
                    workspace[csprojPath] = Encoding.UTF8.GetBytes(csprojContent);
                }
            }

            var backendAppDbContextPath = "backend/AppDbContext.cs";
            if (workspace.TryGetValue(backendAppDbContextPath, out var dbContextBytes))
            {
                var originalDb = Encoding.UTF8.GetString(dbContextBytes);
                var updatedDb = RoslynDbContextMerger.MergeDbSets(originalDb, dbSetInjections.ToArray());
                workspace[backendAppDbContextPath] = Encoding.UTF8.GetBytes(updatedDb);
            }

            // 4. Stitch Frontend App.tsx
            if (!isApiOnly)
            {
                var appTsxPath = "frontend/src/App.tsx";
                if (workspace.TryGetValue(appTsxPath, out var appTsxBytes))
                {
                    var appTsxContent = Encoding.UTF8.GetString(appTsxBytes);
                    
                    var sb = new StringBuilder();
                    // Inject collected import paths from manifests
                    sb.AppendLine(string.Join("\n", importInjections));
                    sb.Append(appTsxContent);

                    var currentContent = sb.ToString();

                    // Check if standard placeholders exist
                    bool hasNavPlaceholder = currentContent.Contains("{/* MODULE_NAV_ITEMS_START */}");
                    bool hasViewsPlaceholder = currentContent.Contains("{/* MODULE_VIEWS_START */}");

                    if ((!hasNavPlaceholder || !hasViewsPlaceholder) && _llmProvider != null)
                    {
                        onLog?.Invoke("[AssemblyEngine] Placeholders missing in App.tsx. Invoking AI-assisted deterministic stitching...");
                        try
                        {
                            var mergeInstruction = $"Please import the modules and merge navigation links: {string.Join(", ", navInjections)} and views: {string.Join(", ", viewInjections)} into the App.tsx file. If custom tabs or sidebar layouts are present, place the links and render blocks inside them naturally.";
                            currentContent = await _llmProvider.StitchFileAsync(currentContent, mergeInstruction, "Target: React Single Page Dashboard");
                        }
                        catch (Exception ex)
                        {
                            onLog?.Invoke($"[AssemblyEngine] AI stitching failed: {ex.Message}. Falling back to standard replace.");
                        }
                    }
                    else
                    {
                        currentContent = currentContent.Replace(
                            "{/* MODULE_NAV_ITEMS_START */}\n          {/* MODULE_NAV_ITEMS_END */}",
                            string.Join("\n          ", navInjections.ToList())
                        );

                        currentContent = currentContent.Replace(
                            "{/* MODULE_VIEWS_START */}\n          {/* MODULE_VIEWS_END */}",
                            string.Join("\n          ", viewInjections.ToList())
                        );
                    }

                    workspace[appTsxPath] = Encoding.UTF8.GetBytes(currentContent);
                }
            }

            onLog?.Invoke("[In-Memory] Generating Software Bill of Materials (SBOM)...");
            var sbomJson = JsonSerializer.Serialize(sbom, AssemblyJsonContext.Default.Sbom);
            workspace["sbom.json"] = Encoding.UTF8.GetBytes(sbomJson);

            // Run registered plugins in-memory
            foreach (var plugin in _plugins)
            {
                try
                {
                    await plugin.ExecuteInMemoryAsync(workspace, blueprint, onLog);
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"[Plugin Error] Failed to execute {plugin.Name} in-memory: {ex.Message}");
                }
            }

            // Verify builds inside AssemblyCompiler if LLM is provided (Self-Healing Loop)
            if (_llmProvider != null && workspace.ContainsKey(backendAppDbContextPath))
            {
                onLog?.Invoke("[AssemblyEngine] Validating compiler safety check (Phase 3 self-healing validation)...");
                // Check if DbContext parses successfully using Roslyn
                var csharpVerificationTree = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(workspace[backendAppDbContextPath]));
                var diagnostics = csharpVerificationTree.GetDiagnostics();
                if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    onLog?.Invoke("[AssemblyEngine] AST Diagnostics error detected. Triggering AI self-healing recovery loop...");
                    var offendingCode = Encoding.UTF8.GetString(workspace[backendAppDbContextPath]);
                    var errorLogs = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage()));
                    var healedCode = await _llmProvider.StitchFileAsync(offendingCode, "Fix code errors: " + errorLogs, "DbContext syntax recovery");
                    workspace[backendAppDbContextPath] = Encoding.UTF8.GetBytes(healedCode);
                }
            }

            onLog?.Invoke("[In-Memory] Compressing final workspace to stream...");
            using var zipMs = new MemoryStream();
            using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, true))
            {
                foreach (var file in workspace)
                {
                    // Create entry inside zip with app prefix path to mirror previous zip structure
                    var entryPath = $"{cleanAppName}/{file.Key}";
                    var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(file.Value, 0, file.Value.Length);
                }
            }

            zipMs.Position = 0;
            onLog?.Invoke("[In-Memory] Streaming artifact to remote Output Store...");
            await outputStore.StoreArtifactAsync(jobId, zipMs);

            onLog?.Invoke($"[In-Memory] Assembly completed successfully! Artifact is now available for download.");
            return new AssemblyResult { Success = true, SbomJson = sbomJson };
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"[Assembly Failure] {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Processes a single module completely in memory: fetches OCI dictionary,
    /// maps controllers, collects nav/view/import/dbset injection strings, and
    /// merges module views/controllers into the shared workspace.
    /// </summary>
    private async Task ProcessModuleInMemoryAsync(
        string modName,
        bool isApiOnly,
        ConcurrentDictionary<string, byte[]> workspace,
        ConcurrentBag<string> navInjections,
        ConcurrentBag<string> viewInjections,
        ConcurrentBag<string> importInjections,
        ConcurrentBag<string> dbSetInjections,
        ConcurrentBag<SbomComponent> sbomEntries,
        Action<string>? onLog)
    {
        using var activity = CompilerActivitySource.StartActivity("ProcessModule");
        activity?.SetTag("module.name", modName);

        Dictionary<string, byte[]> modFiles;
        string digest;
        try
        {
            onLog?.Invoke($"[parallel-mem] Pulling module '{modName}' from Vault...");
            var result = await _vaultRepository.FetchModuleInMemoryAsync(modName, "latest");
            modFiles = result.files;
            digest = result.digest;

            sbomEntries.Add(new SbomComponent
            {
                Name    = $"modules/{modName.ToLower()}",
                Version = "latest",
                Digest  = digest
            });
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"[parallel-mem] Skipping module '{modName}': {ex.Message}");
            return;
        }

        var manifestKey = "manifest.json";
        if (!modFiles.TryGetValue(manifestKey, out var manifestBytes)) return;

        var manifestJson = Encoding.UTF8.GetString(manifestBytes);
        var manifest = JsonSerializer.Deserialize(manifestJson, AssemblyJsonContext.Default.ModuleManifest);
        if (manifest == null) return;

        // ── Backend: Copy controllers into workspace in-memory ──
        if (manifest.Backend?.Controllers != null)
        {
            foreach (var controllerFile in manifest.Backend.Controllers)
            {
                var cleanFileKey = controllerFile.TrimStart('/');
                if (modFiles.TryGetValue(cleanFileKey, out var controllerBytes))
                {
                    var destKey = $"backend/Controllers/{Path.GetFileName(cleanFileKey)}";
                    workspace[destKey] = controllerBytes;
                }
            }
        }

        if (!string.IsNullOrEmpty(manifest.Backend?.DbSets))
            dbSetInjections.Add(manifest.Backend.DbSets);

        // ── Frontend: Copy views and collect nav/imports in-memory ──
        if (!isApiOnly && manifest.Frontend != null)
        {
            if (!string.IsNullOrEmpty(manifest.Frontend.Nav))
                navInjections.Add(manifest.Frontend.Nav);

            if (!string.IsNullOrEmpty(manifest.Frontend.Imports))
                importInjections.Add(manifest.Frontend.Imports);

            if (!string.IsNullOrEmpty(manifest.Frontend.Views))
            {
                var cleanViewKey = manifest.Frontend.Views.TrimStart('/');
                if (modFiles.TryGetValue(cleanViewKey, out var viewBytes))
                {
                    var destKey = $"frontend/src/modules/{manifest.Name}/react-views.tsx";
                    workspace[destKey] = viewBytes;
                }

                var componentName = manifest.Name;
                var viewTagName   = componentName + "View";

                if (componentName.Equals("Auth", StringComparison.OrdinalIgnoreCase))
                {
                    viewInjections.Add("{activeTab === 'login' && <LoginView login={login} request={request} setActiveTab={setActiveTab} />}");
                    viewInjections.Add("{activeTab === 'register' && <RegisterView login={login} request={request} setActiveTab={setActiveTab} />}");
                }
                else if (componentName.Equals("OktaAuth", StringComparison.OrdinalIgnoreCase))
                {
                    viewInjections.Add("{activeTab === 'oktaauth' && <OktaAuthView login={login} request={request} setActiveTab={setActiveTab} />}");
                }
                else if (componentName.Equals("EntraIdAuth", StringComparison.OrdinalIgnoreCase))
                {
                    viewInjections.Add("{activeTab === 'entraidauth' && <EntraIdAuthView login={login} request={request} setActiveTab={setActiveTab} />}");
                }
                else if (componentName.Equals("GithubAuth", StringComparison.OrdinalIgnoreCase))
                {
                    viewInjections.Add("{activeTab === 'githubauth' && <GithubAuthView login={login} request={request} setActiveTab={setActiveTab} />}");
                }
                else if (componentName.Equals("Items", StringComparison.OrdinalIgnoreCase))
                {
                    viewInjections.Add("{activeTab === 'items' && <ItemsView request={request} token={token} />}");
                }
                else
                {
                    viewInjections.Add($"{{activeTab === '{componentName.ToLower()}' && <{viewTagName} request={{request}} activeTab={{activeTab}} />}}");
                }
            }
        }
    }
}
