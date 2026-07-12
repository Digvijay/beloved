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

    // Per-manifest component props passed from the parallel fan-out
    private sealed record ModuleStitchResult(
        List<string> NavInjections,
        List<string> ViewInjections,
        List<string> ImportInjections,
        List<string> DbSetInjections,
        SbomComponent? SbomEntry);

    public AssemblyCompiler(IVaultRepository vaultRepository, IEnumerable<IAssemblyPlugin> plugins)
    {
        _vaultRepository = vaultRepository;
        _plugins = plugins;
    }

    public async Task<AssemblyResult> AssembleAsync(string blueprintJson, string jobId, IOutputStore outputStore, Action<string>? onLog = null)
    {
        var blueprint = JsonSerializer.Deserialize(blueprintJson, AssemblyJsonContext.Default.Blueprint);
        if (blueprint == null) return new AssemblyResult { Success = false };

        onLog?.Invoke($"Starting assembly for application: {blueprint.AppName}");

        var cleanAppName = blueprint.AppName.Replace(" ", "");
        var tempWorkspace = Path.Combine(Path.GetTempPath(), "beloved_workspace_" + Guid.NewGuid().ToString());
        var appPath = Path.Combine(tempWorkspace, cleanAppName);
        var frontendDest = Path.Combine(appPath, "frontend");
        var backendDest = Path.Combine(appPath, "backend");
        var tempModulesDir = Path.Combine(tempWorkspace, "modules");

        var isApiOnly = blueprint.Target.Equals("ApiOnly", StringComparison.OrdinalIgnoreCase);

        // Create directories
        Directory.CreateDirectory(appPath);
        if (!isApiOnly)
        {
            Directory.CreateDirectory(frontendDest);
        }
        Directory.CreateDirectory(backendDest);
        Directory.CreateDirectory(tempModulesDir);

        var sbom = new Sbom { ProjectName = cleanAppName, JobId = jobId };

        // 1. Fetch base templates concurrently — frontend + backend in parallel
        onLog?.Invoke("Fetching base templates from OCI Vault in parallel...");
        var templateTasks = new List<Task<(string targetDirectory, string digest)>>();
        if (!isApiOnly) templateTasks.Add(_vaultRepository.FetchTemplateAsync("react-frontend", frontendDest));
        templateTasks.Add(_vaultRepository.FetchTemplateAsync("dotnet-backend", backendDest));
        var templateResults = await Task.WhenAll(templateTasks);

        if (!isApiOnly) sbom.Components.Add(new SbomComponent { Name = "templates/react-frontend", Version = "latest", Digest = templateResults[0].digest });
        sbom.Components.Add(new SbomComponent { Name = "templates/dotnet-backend", Version = "latest", Digest = templateResults[^1].digest });

        // Thread-safe accumulators — written to from concurrent module tasks
        var navInjections    = new ConcurrentBag<string>();
        var viewInjections   = new ConcurrentBag<string>();
        var importInjections = new ConcurrentBag<string>();
        var dbSetInjections  = new ConcurrentBag<string>();
        var sbomEntries      = new ConcurrentBag<SbomComponent>();

        try
        {
            // 3. Fan-out: fetch and process ALL modules in parallel
            onLog?.Invoke($"Fetching {blueprint.Modules.Count} module(s) from Vault in parallel...");
            await Task.WhenAll(blueprint.Modules.Select(modName =>
                ProcessModuleAsync(
                    modName, tempModulesDir, frontendDest, backendDest, isApiOnly,
                    navInjections, viewInjections, importInjections, dbSetInjections, sbomEntries,
                    onLog)));

            foreach (var entry in sbomEntries) sbom.Components.Add(entry);
            
            onLog?.Invoke("Stitching module definitions into core engine...");
            
            // 4. Stitch Backend Program.cs
            var programPath = Path.Combine(backendDest, "Program.cs");
            if (File.Exists(programPath))
            {
                var programContent = await File.ReadAllTextAsync(programPath);
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
                await File.WriteAllTextAsync(programPath, sb.ToString());

                // Update csproj with database package references if needed
                var csprojPath = Path.Combine(backendDest, "dotnet-backend.csproj");
                if (File.Exists(csprojPath))
                {
                    var csprojContent = await File.ReadAllTextAsync(csprojPath);
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
                    await File.WriteAllTextAsync(csprojPath, csprojContent);
                }
            }

            var backendAppDbContextPath = Path.Combine(backendDest, "AppDbContext.cs");
            if (File.Exists(backendAppDbContextPath))
            {
                var originalDb = await File.ReadAllTextAsync(backendAppDbContextPath);
                var sb = new StringBuilder(originalDb);
                sb.Replace("/* INJECT_DBSETS */", string.Join("\n    ", dbSetInjections.ToList()));
                await File.WriteAllTextAsync(backendAppDbContextPath, sb.ToString());
            }

            // 5. Stitch Frontend App.tsx
            var appTsxPath = Path.Combine(frontendDest, "src", "App.tsx");
            if (File.Exists(appTsxPath))
            {
                var appTsxContent = await File.ReadAllTextAsync(appTsxPath);
                
                var sb = new StringBuilder();
                // Inject collected import paths from manifests
                sb.AppendLine(string.Join("\n", importInjections));
                sb.Append(appTsxContent);

                sb.Replace(
                    "{/* MODULE_NAV_ITEMS_START */}\n          {/* MODULE_NAV_ITEMS_END */}",
                    string.Join("\n          ", navInjections.ToList())
                );

                sb.Replace(
                    "{/* MODULE_VIEWS_START */}\n          {/* MODULE_VIEWS_END */}",
                    string.Join("\n          ", viewInjections.ToList())
                );

                await File.WriteAllTextAsync(appTsxPath, sb.ToString());
            }

            onLog?.Invoke("Generating Software Bill of Materials (SBOM)...");
            var sbomJson = JsonSerializer.Serialize(sbom, AssemblyJsonContext.Default.Sbom);
            await File.WriteAllTextAsync(Path.Combine(appPath, "sbom.json"), sbomJson);

            // Run registered plugins
            foreach (var plugin in _plugins)
            {
                try
                {
                    await plugin.ExecuteAsync(appPath, blueprint, onLog);
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"[Plugin Error] Failed to execute {plugin.Name}: {ex.Message}");
                }
            }

            onLog?.Invoke("Compressing final artifacts...");
            var zipPath = Path.Combine(tempWorkspace, $"{jobId}.zip");
            ZipFile.CreateFromDirectory(appPath, zipPath);

            onLog?.Invoke("Streaming artifacts to remote Output Store...");
            using (var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await outputStore.StoreArtifactAsync(jobId, zipStream);
            }

            onLog?.Invoke($"Assembly completed successfully! Artifact is now available for download.");
            return new AssemblyResult { Success = true, SbomJson = sbomJson };
        }
        finally
        {
            if (Directory.Exists(tempWorkspace))
            {
                Directory.Delete(tempWorkspace, true);
            }
        }
    }

    /// <summary>
    /// Processes a single module: fetches from OCI, copies controllers, collects
    /// nav/view/import/dbset injection strings. Designed to run concurrently
    /// for any number of modules without synchronisation issues.
    /// </summary>
    private async Task ProcessModuleAsync(
        string modName,
        string tempModulesDir,
        string frontendDest,
        string backendDest,
        bool isApiOnly,
        ConcurrentBag<string> navInjections,
        ConcurrentBag<string> viewInjections,
        ConcurrentBag<string> importInjections,
        ConcurrentBag<string> dbSetInjections,
        ConcurrentBag<SbomComponent> sbomEntries,
        Action<string>? onLog)
    {
        var modDir = Path.Combine(tempModulesDir, modName.ToLower());
        try
        {
            onLog?.Invoke($"[parallel] Pulling module '{modName}' from Vault...");
            var modArtifact = await _vaultRepository.FetchModuleAsync(modName, "latest", modDir);
            sbomEntries.Add(new SbomComponent
            {
                Name    = $"modules/{modName.ToLower()}",
                Version = "latest",
                Digest  = modArtifact.digest
            });
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"[parallel] Skipping module '{modName}': {ex.Message}");
            return; // Non-fatal: skip unavailable modules
        }

        if (!Directory.Exists(modDir)) return;

        var manifestPath = Path.Combine(modDir, "manifest.json");
        if (!File.Exists(manifestPath)) return;

        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize(manifestJson, AssemblyJsonContext.Default.ModuleManifest);
        if (manifest == null) return;

        // ── Backend: copy controllers (each module writes to its own filename, safe) ──
        if (manifest.Backend?.Controllers != null)
        {
            foreach (var controllerFile in manifest.Backend.Controllers)
            {
                var source = Path.Combine(modDir, controllerFile);
                if (File.Exists(source))
                {
                    var dest = Path.Combine(backendDest, "Controllers", Path.GetFileName(source));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(source, dest, overwrite: true);
                }
            }
        }

        if (!string.IsNullOrEmpty(manifest.Backend?.DbSets))
            dbSetInjections.Add(manifest.Backend.DbSets);

        // ── Frontend: copy view files and collect injection strings ──
        if (!isApiOnly && manifest.Frontend != null)
        {
            if (!string.IsNullOrEmpty(manifest.Frontend.Nav))
                navInjections.Add(manifest.Frontend.Nav);

            if (!string.IsNullOrEmpty(manifest.Frontend.Imports))
                importInjections.Add(manifest.Frontend.Imports);

            if (!string.IsNullOrEmpty(manifest.Frontend.Views))
            {
                var viewSource = Path.Combine(modDir, manifest.Frontend.Views);
                if (File.Exists(viewSource))
                {
                    // Each module writes to its own named subdirectory — no collision
                    var viewsDestDir = Path.Combine(frontendDest, "src", "modules", manifest.Name);
                    Directory.CreateDirectory(viewsDestDir);
                    File.Copy(viewSource, Path.Combine(viewsDestDir, "react-views.tsx"), overwrite: true);
                }

                // Dynamic view-binding: derive tab key and component tag from manifest name
                var componentName = manifest.Name;
                var viewTagName   = componentName + "View";

                if (componentName.Equals("Auth", StringComparison.OrdinalIgnoreCase))
                {
                    viewInjections.Add("{activeTab === 'login' && <LoginView login={login} request={request} setActiveTab={setActiveTab} />}");
                    viewInjections.Add("{activeTab === 'register' && <RegisterView login={login} request={request} setActiveTab={setActiveTab} />}");
                }
                else if (componentName.Equals("Items", StringComparison.OrdinalIgnoreCase))
                {
                    viewInjections.Add("{activeTab === 'items' && <ItemsView request={request} token={token} />}");
                }
                else
                {
                    // Fully generic: any future module is automatically wired without compiler changes
                    viewInjections.Add($"{{activeTab === '{componentName.ToLower()}' && <{viewTagName} request={{request}} activeTab={{activeTab}} />}}");
                }
            }
        }
    }
}
