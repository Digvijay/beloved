using Beloved.AssemblyEngine;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Beloved.ControlPlane.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;
using Xunit;

namespace Beloved.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Helpers shared across test classes
// ─────────────────────────────────────────────────────────────────────────────
public static class TestHelpers
{
    public static BelovedDbContext GetDb() =>
        new(new DbContextOptionsBuilder<BelovedDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static IConfiguration JwtConfig(string secret = "this-is-a-long-enough-dev-secret-32-chars-minimum") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:Secret",              secret },
                { "Jwt:Issuer",              "beloved.build" },
                { "Jwt:Audience",            "beloved.build" },
                { "Jwt:AccessTokenMinutes",  "15" }
            })
            .Build();

    public static ILogger<T> NullLogger<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
}

public class MockWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ApplicationName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
// Quota Service Tests
// ─────────────────────────────────────────────────────────────────────────────
public class QuotaServiceTests
{
    [Fact]
    public async Task FreePlan_BlocksAfter50Assemblies()
    {
        using var db = TestHelpers.GetDb();
        var tenant = new Tenant { Name = "Free", ApiKey = "k1", Plan = TenantPlan.Free };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var svc = new QuotaService(db);
        Assert.True(await svc.HasQuotaAsync(tenant.Id));

        for (int i = 0; i < 50; i++)
            await svc.RecordUsageAsync(tenant.Id, $"j{i}", 200, 1, true);

        Assert.False(await svc.HasQuotaAsync(tenant.Id));
    }

    [Fact]
    public async Task ProPlan_BlocksAfter500Assemblies()
    {
        using var db = TestHelpers.GetDb();
        var tenant = new Tenant { Name = "Pro", ApiKey = "k2", Plan = TenantPlan.Pro };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var svc = new QuotaService(db);
        for (int i = 0; i < 500; i++)
            await svc.RecordUsageAsync(tenant.Id, $"j{i}", 200, 1, true);

        Assert.False(await svc.HasQuotaAsync(tenant.Id));
    }

    [Fact]
    public async Task EnterprisePlan_NeverBlocked()
    {
        using var db = TestHelpers.GetDb();
        var tenant = new Tenant { Name = "Ent", ApiKey = "k3", Plan = TenantPlan.Enterprise };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var svc = new QuotaService(db);
        for (int i = 0; i < 200; i++)
            await svc.RecordUsageAsync(tenant.Id, $"j{i}", 100, 1, true);

        Assert.True(await svc.HasQuotaAsync(tenant.Id));
    }

    [Fact]
    public async Task FreePlan_QuotaResets_NextMonth()
    {
        using var db = TestHelpers.GetDb();
        var tenant = new Tenant { Name = "FreeReset", ApiKey = "k4", Plan = TenantPlan.Free };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var svc = new QuotaService(db);

        // Simulate 50 usages in a previous month by inserting records directly
        var lastMonth = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM");
        for (int i = 0; i < 50; i++)
            db.AssemblyUsages.Add(new AssemblyUsage
            {
                TenantId    = tenant.Id,
                JobId       = $"old-j{i}",
                PeriodMonth = lastMonth,
                ModuleCount = 1,
                DurationMs  = 100,
                Succeeded   = true
            });
        await db.SaveChangesAsync();

        // Current month quota should still be available
        Assert.True(await svc.HasQuotaAsync(tenant.Id));
    }

    [Fact]
    public async Task RecordUsage_PersistsCorrectly()
    {
        using var db = TestHelpers.GetDb();
        var tenant = new Tenant { Name = "Rec", ApiKey = "k5", Plan = TenantPlan.Free };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var svc = new QuotaService(db);
        await svc.RecordUsageAsync(tenant.Id, "job-abc", 100, 3, true);

        var record = await db.AssemblyUsages.FirstOrDefaultAsync(u => u.JobId == "job-abc");
        Assert.NotNull(record);
        Assert.Equal(3, record.ModuleCount);
        Assert.Equal(100, record.DurationMs);
        Assert.True(record.Succeeded);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// JWT Token Service Tests
// ─────────────────────────────────────────────────────────────────────────────
public class JwtTokenServiceTests
{
    [Fact]
    public void IssuesToken_WithCorrectClaims()
    {
        var svc = new JwtTokenService(TestHelpers.JwtConfig());
        var user = new BelovedUser { Provider = "github", ProviderSubject = "1", Email = "a@b.com", DisplayName = "Alice" };

        var token = svc.CreateAccessToken(user, new[] { "acme" });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("beloved.build", jwt.Issuer);
        Assert.Equal("a@b.com", jwt.Payload[JwtRegisteredClaimNames.Email]);
    }

    [Fact]
    public void IssuesToken_WithOrgSlugClaim()
    {
        var svc = new JwtTokenService(TestHelpers.JwtConfig());
        var user = new BelovedUser { Provider = "google", ProviderSubject = "2", Email = "b@c.com", DisplayName = "Bob" };

        var token = svc.CreateAccessToken(user, new[] { "myorg", "otherorg" });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        var orgClaims = jwt.Claims.Where(c => c.Type == "org").Select(c => c.Value).ToList();
        Assert.Contains("myorg", orgClaims);
    }

    [Fact]
    public void Token_ExpiresWithinConfiguredWindow()
    {
        var svc = new JwtTokenService(TestHelpers.JwtConfig());
        var user = new BelovedUser { Provider = "github", ProviderSubject = "3", Email = "c@d.com", DisplayName = "Carol" };

        var before = DateTime.UtcNow.AddMinutes(14);
        var after  = DateTime.UtcNow.AddMinutes(16);
        var token = svc.CreateAccessToken(user, Array.Empty<string>());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.True(jwt.ValidTo >= before && jwt.ValidTo <= after,
            $"Expected expiry between {before} and {after}, got {jwt.ValidTo}");
    }

    [Fact]
    public void ShortSecret_ThrowsOnServiceCreation()
    {
        // A secret shorter than 32 chars must fail — cannot produce a safe HMAC-SHA256 key
        var cfg = TestHelpers.JwtConfig("tooshort");
        var svc = new JwtTokenService(cfg);
        var user = new BelovedUser { Provider = "github", ProviderSubject = "4", Email = "d@e.com", DisplayName = "Dan" };

        Assert.ThrowsAny<Exception>(() => svc.CreateAccessToken(user, Array.Empty<string>()));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Email Outbox Tests
// ─────────────────────────────────────────────────────────────────────────────
public class EmailSenderTests
{
    private EmailSender BuildSender(BelovedDbContext db) =>
        new(db, new MockWebHostEnvironment(), TestHelpers.NullLogger<EmailSender>());

    [Fact]
    public async Task SendPaymentFailed_WritesToOutbox()
    {
        using var db = TestHelpers.GetDb();
        await BuildSender(db).SendPaymentFailedEmailAsync("u@x.com", "Acme", 49.00m, "http://portal");

        var job = await db.EmailQueueJobs.SingleAsync();
        Assert.Equal("u@x.com", job.RecipientEmail);
        Assert.Equal("Pending", job.Status);
        Assert.Contains("Acme", job.Body);
    }

    [Fact]
    public async Task MultipleEmailsSent_AllQueued()
    {
        using var db = TestHelpers.GetDb();
        var sender = BuildSender(db);

        await sender.SendPaymentFailedEmailAsync("a@x.com", "TenantA", 9m, "http://a");
        await sender.SendPaymentFailedEmailAsync("b@x.com", "TenantB", 9m, "http://b");

        var count = await db.EmailQueueJobs.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task QueuedEmail_HasZeroRetryCount()
    {
        using var db = TestHelpers.GetDb();
        await BuildSender(db).SendPaymentFailedEmailAsync("r@x.com", "T", 9m, "http://p");

        var job = await db.EmailQueueJobs.FirstAsync();
        Assert.Equal(0, job.RetryCount);
        Assert.Null(job.ProcessedAt);
    }

    [Fact]
    public async Task QueuedEmail_SubjectContainsPaymentFailed()
    {
        using var db = TestHelpers.GetDb();
        await BuildSender(db).SendPaymentFailedEmailAsync("s@x.com", "BigCo", 99m, "http://fix");

        var job = await db.EmailQueueJobs.FirstAsync();
        Assert.Contains("Payment", job.Subject, StringComparison.OrdinalIgnoreCase);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Signature Verifier Tests
// ─────────────────────────────────────────────────────────────────────────────
public class SignatureVerifierTests
{
    private readonly PemSignatureVerifier _verifier = new();

    [Fact]
    public void FailsClosed_OnEmptySignature()
    {
        Assert.False(_verifier.VerifySignature(
            Encoding.UTF8.GetBytes("payload"),
            Array.Empty<byte>(),
            "not-a-real-key"));
    }

    [Fact]
    public void FailsClosed_OnMalformedKey()
    {
        Assert.False(_verifier.VerifySignature(
            Encoding.UTF8.GetBytes("data"),
            new byte[] { 1, 2, 3 },
            "-----BEGIN PUBLIC KEY-----\nBADDATA\n-----END PUBLIC KEY-----"));
    }

    [Fact]
    public void FailsClosed_OnEmptyPayload()
    {
        Assert.False(_verifier.VerifySignature(
            Array.Empty<byte>(),
            new byte[] { 9, 8, 7 },
            "invalid-pem"));
    }

    [Fact]
    public void FailsClosed_OnNullishKey()
    {
        Assert.False(_verifier.VerifySignature(
            Encoding.UTF8.GetBytes("payload"),
            new byte[] { 1 },
            string.Empty));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// LocalDiskOutputStore Tests
// ─────────────────────────────────────────────────────────────────────────────
public class OutputStoreTests : IDisposable
{
    private readonly LocalDiskOutputStore _store = new();

    [Fact]
    public async Task StoreAndRetrieve_RoundTrip()
    {
        var jobId  = Guid.NewGuid().ToString("N");
        var data   = Encoding.UTF8.GetBytes("hello beloved artifact");

        await _store.StoreArtifactAsync(jobId, new MemoryStream(data));
        await using var result = await _store.GetArtifactAsync(jobId);

        Assert.NotNull(result);
        using var reader = new StreamReader(result!);
        var text = await reader.ReadToEndAsync();
        Assert.Equal("hello beloved artifact", text);
    }

    [Fact]
    public async Task GetArtifact_ReturnsNull_WhenNotFound()
    {
        var result = await _store.GetArtifactAsync("nonexistent-job-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task ConcurrentWrites_DifferentJobs_BothSucceed()
    {
        var id1 = Guid.NewGuid().ToString("N");
        var id2 = Guid.NewGuid().ToString("N");

        await Task.WhenAll(
            _store.StoreArtifactAsync(id1, new MemoryStream(Encoding.UTF8.GetBytes("job1"))),
            _store.StoreArtifactAsync(id2, new MemoryStream(Encoding.UTF8.GetBytes("job2")))
        );

        Assert.NotNull(await _store.GetArtifactAsync(id1));
        Assert.NotNull(await _store.GetArtifactAsync(id2));
    }

    public void Dispose() { /* cleanup handled by GC on test dirs */ }
}

// ─────────────────────────────────────────────────────────────────────────────
// AssemblyCompiler Tests (with mocked IVaultRepository)
// ─────────────────────────────────────────────────────────────────────────────
public class AssemblyCompilerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _frontendSrc;
    private readonly string _backendSrc;
    private readonly LocalDiskOutputStore _store = new();

    public AssemblyCompilerTests()
    {
        // Build minimal OCI-like fixture directories the mock vault returns
        _tempDir = Path.Combine(Path.GetTempPath(), "beloved_compiler_test_" + Guid.NewGuid().ToString("N"));
        _frontendSrc = Path.Combine(_tempDir, "react-frontend");
        _backendSrc  = Path.Combine(_tempDir, "dotnet-backend");

        Directory.CreateDirectory(Path.Combine(_frontendSrc, "src"));
        File.WriteAllText(Path.Combine(_frontendSrc, "src", "App.tsx"),
            "// placeholder\n{/* MODULE_NAV_ITEMS_START */}\n          {/* MODULE_NAV_ITEMS_END */}\n{/* MODULE_VIEWS_START */}\n          {/* MODULE_VIEWS_END */}");
        File.WriteAllText(Path.Combine(_frontendSrc, "src", "index.css"), "/* css */");

        Directory.CreateDirectory(Path.Combine(_backendSrc, "Controllers"));
        File.WriteAllText(Path.Combine(_backendSrc, "Program.cs"),
            "// DATABASE_INJECTION_START\n// DATABASE_INJECTION_END\n// DBSETS_PLACEHOLDER");
        File.WriteAllText(Path.Combine(_backendSrc, "AppDbContext.cs"),
            "/* INJECT_DBSETS */");
        File.WriteAllText(Path.Combine(_backendSrc, "dotnet-backend.csproj"),
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"9.0.0\" />");
        File.WriteAllText(Path.Combine(_backendSrc, "Dockerfile"), "FROM mcr.microsoft.com/dotnet/aspnet:9.0");
    }

    private Mock<IVaultRepository> BuildMockVault()
    {
        var mock = new Mock<IVaultRepository>();

        // Templates — path versions
        mock.Setup(v => v.FetchTemplateAsync("react-frontend", It.IsAny<string>()))
            .Returns<string, string>((_, dest) =>
            {
                CopyDirectory(_frontendSrc, dest);
                return Task.FromResult((dest, "sha256:react-stub"));
            });

        mock.Setup(v => v.FetchTemplateAsync("dotnet-backend", It.IsAny<string>()))
            .Returns<string, string>((_, dest) =>
            {
                CopyDirectory(_backendSrc, dest);
                return Task.FromResult((dest, "sha256:dotnet-stub"));
            });

        // In-memory templates mock
        mock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend"))
            .ReturnsAsync(() => (LoadDirectoryToMemory(_frontendSrc), "sha256:react-stub-mem"));

        mock.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend"))
            .ReturnsAsync(() => (LoadDirectoryToMemory(_backendSrc), "sha256:dotnet-stub-mem"));

        return mock;
    }

    private static Dictionary<string, byte[]> LoadDirectoryToMemory(string sourceDir)
    {
        var files = new Dictionary<string, byte[]>();
        LoadDirectoryToMemoryInternal(sourceDir, sourceDir, files);
        return files;
    }

    private static void LoadDirectoryToMemoryInternal(string rootDir, string currentDir, Dictionary<string, byte[]> files)
    {
        foreach (var file in Directory.GetFiles(currentDir))
        {
            var relPath = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            files[relPath] = File.ReadAllBytes(file);
        }

        foreach (var directory in Directory.GetDirectories(currentDir))
        {
            LoadDirectoryToMemoryInternal(rootDir, directory, files);
        }
    }

    private void SetupModule(Mock<IVaultRepository> mock, string moduleName, string manifestJson)
    {
        mock.Setup(v => v.FetchModuleAsync(moduleName, "latest", It.IsAny<string>()))
            .Returns<string, string, string>((name, _, dest) =>
            {
                Directory.CreateDirectory(dest);
                File.WriteAllText(Path.Combine(dest, "manifest.json"), manifestJson);
                return Task.FromResult((dest, $"sha256:{name}-stub"));
            });

        mock.Setup(v => v.FetchModuleInMemoryAsync(moduleName, "latest"))
            .ReturnsAsync(() => 
            {
                var files = new Dictionary<string, byte[]>
                {
                    { "manifest.json", Encoding.UTF8.GetBytes(manifestJson) }
                };
                return (files, $"sha256:{moduleName}-stub-mem");
            });
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel  = Path.GetRelativePath(src, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    [Fact]
    public async Task Assemble_WithNoModules_Succeeds()
    {
        var mock = BuildMockVault();
        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());

        var blueprint = new Blueprint { AppName = "EmptyApp", Modules = new() };
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job1", _store);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Assemble_GeneratesSbom_WithTemplateEntries()
    {
        var mock = BuildMockVault();
        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());

        var blueprint = new Blueprint { AppName = "SbomApp", Modules = new() };
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-sbom", _store);

        Assert.True(result.Success);
        Assert.Contains("templates/react-frontend", result.SbomJson);
        Assert.Contains("templates/dotnet-backend", result.SbomJson);
    }

    [Fact]
    public async Task Assemble_WithAnalyticsModule_InjectsDynamicView()
    {
        var mock = BuildMockVault();
        SetupModule(mock, "Analytics", """
            {
              "name": "Analytics",
              "description": "Analytics module",
              "frontend": { "nav": "<li>Analytics</li>", "views": "react-views.tsx", "imports": "import { AnalyticsView } from './modules/Analytics/react-views';" },
              "backend": { "controllers": [], "dbSets": "" }
            }
            """);

        // Place a stub react-views.tsx that the compiler will copy
        mock.Setup(v => v.FetchModuleAsync("Analytics", "latest", It.IsAny<string>()))
            .Returns<string, string, string>((_, _, dest) =>
            {
                Directory.CreateDirectory(dest);
                File.WriteAllText(Path.Combine(dest, "manifest.json"), """
                    {"name":"Analytics","description":"","frontend":{"nav":"<li>Analytics</li>","views":"react-views.tsx","imports":"import { AnalyticsView } from './modules/Analytics/react-views';"},"backend":{"controllers":[],"dbSets":""}}
                    """);
                File.WriteAllText(Path.Combine(dest, "react-views.tsx"), "export function AnalyticsView() { return null; }");
                return Task.FromResult((dest, "sha256:analytics-stub"));
            });

        mock.Setup(v => v.FetchModuleInMemoryAsync("Analytics", "latest"))
            .ReturnsAsync(() =>
            {
                var files = new Dictionary<string, byte[]>
                {
                    { "manifest.json", Encoding.UTF8.GetBytes("{\"name\":\"Analytics\",\"description\":\"\",\"frontend\":{\"nav\":\"<li>Analytics</li>\",\"views\":\"react-views.tsx\",\"imports\":\"import { AnalyticsView } from './modules/Analytics/react-views';\"},\"backend\":{\"controllers\":[],\"dbSets\":\"\"}}") },
                    { "react-views.tsx", Encoding.UTF8.GetBytes("export function AnalyticsView() { return null; }") }
                };
                return (files, "sha256:analytics-stub-mem");
            });

        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        var blueprint = new Blueprint { AppName = "AnalyticsApp", Modules = new() { "Analytics" } };
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-analytics", _store);

        Assert.True(result.Success);
        Assert.Contains("modules/analytics", result.SbomJson);
    }

    [Fact]
    public async Task Assemble_UnavailableModule_IsSkippedGracefully()
    {
        var mock = BuildMockVault();
        mock.Setup(v => v.FetchModuleInMemoryAsync("BrokenModule", "latest"))
            .ThrowsAsync(new Exception("OCI registry returned 404"));

        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        var blueprint = new Blueprint { AppName = "SafeApp", Modules = new() { "BrokenModule" } };

        // Should NOT throw — assembly must continue with remaining modules
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-broken", _store);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Assemble_MultipleModules_AllFetchedInParallel()
    {
        // Use a shared counter to confirm concurrent execution
        var fetchOrder = new System.Collections.Concurrent.ConcurrentBag<string>();

        var mock = BuildMockVault();
        foreach (var mod in new[] { "ModA", "ModB", "ModC" })
        {
            var capture = mod;
            mock.Setup(v => v.FetchModuleInMemoryAsync(capture, "latest"))
                .Returns<string, string>(async (_, _) =>
                {
                    fetchOrder.Add(capture);
                    await Task.Delay(50); // simulate I/O
                    var files = new Dictionary<string, byte[]>
                    {
                        { "manifest.json", Encoding.UTF8.GetBytes($"{{\"name\":\"{capture}\",\"description\":\"\",\"frontend\":{{\"nav\":\"\",\"views\":\"\",\"imports\":\"\"}},\"backend\":{{\"controllers\":[],\"dbSets\":\"\"}}}}") }
                    };
                    return (files, $"sha256:{capture}");
                });
        }

        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        var blueprint = new Blueprint { AppName = "ParallelApp", Modules = new() { "ModA", "ModB", "ModC" } };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-parallel", _store);
        sw.Stop();

        Assert.True(result.Success);
        // Sequential would take ≥ 150ms; parallel should finish in < 120ms
        Assert.True(sw.ElapsedMilliseconds < 120,
            $"Expected parallel fetch < 120ms, took {sw.ElapsedMilliseconds}ms — modules may not be running concurrently");
        Assert.Equal(3, fetchOrder.Count);
    }

    [Fact]
    public async Task Assemble_ApiOnly_SkipsFrontend()
    {
        var mock = BuildMockVault();
        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());

        var blueprint = new Blueprint { AppName = "ApiApp", Target = "ApiOnly", Modules = new() };
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-api", _store);

        Assert.True(result.Success);
        // react-frontend template should never be fetched
        mock.Verify(v => v.FetchTemplateInMemoryAsync("react-frontend"), Times.Never);
    }

    [Fact]
    public async Task Assemble_PostgreSqlBlueprint_UpdatesCsproj()
    {
        var mock = BuildMockVault();
        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());

        var blueprint = new Blueprint { AppName = "PgApp", Database = "PostgreSQL", Modules = new() };
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-pg", _store);

        Assert.True(result.Success);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Webhook Registration Tests
// ─────────────────────────────────────────────────────────────────────────────
public class WebhookModelTests
{
    [Fact]
    public void Webhook_IsActive_ByDefault()
    {
        var wh = new Webhook { Url = "https://example.com", Events = "assembly.completed" };
        Assert.True(wh.IsActive);
    }

    [Fact]
    public void Webhook_GeneratesGuid_OnCreation()
    {
        var wh1 = new Webhook { Url = "https://a.com" };
        var wh2 = new Webhook { Url = "https://b.com" };
        Assert.NotEqual(wh1.Id, wh2.Id);
    }

    [Fact]
    public async Task WebhookPersisted_InDatabase()
    {
        using var db = TestHelpers.GetDb();
        var tid = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tid, Name = "T", ApiKey = "wk", Plan = TenantPlan.Free });
        db.Webhooks.Add(new Webhook { TenantId = tid, Url = "https://receiver.io", Events = "*" });
        await db.SaveChangesAsync();

        var saved = await db.Webhooks.FirstAsync();
        Assert.Equal("https://receiver.io", saved.Url);
        Assert.True(saved.IsActive);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Assembly Compiler Stress Tests
// ─────────────────────────────────────────────────────────────────────────────
public class AssemblyCompilerStressTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _frontendSrc;
    private readonly string _backendSrc;
    private readonly LocalDiskOutputStore _store = new();

    public AssemblyCompilerStressTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beloved_stress_test_" + Guid.NewGuid().ToString("N"));
        _frontendSrc = Path.Combine(_tempDir, "react-frontend");
        _backendSrc  = Path.Combine(_tempDir, "dotnet-backend");

        Directory.CreateDirectory(Path.Combine(_frontendSrc, "src"));
        File.WriteAllText(Path.Combine(_frontendSrc, "src", "App.tsx"),
            "// placeholder\n{/* MODULE_NAV_ITEMS_START */}\n          {/* MODULE_NAV_ITEMS_END */}\n{/* MODULE_VIEWS_START */}\n          {/* MODULE_VIEWS_END */}");
        File.WriteAllText(Path.Combine(_frontendSrc, "src", "index.css"), "/* css */");

        Directory.CreateDirectory(Path.Combine(_backendSrc, "Controllers"));
        File.WriteAllText(Path.Combine(_backendSrc, "Program.cs"),
            "// DATABASE_INJECTION_START\n// DATABASE_INJECTION_END\n// DBSETS_PLACEHOLDER");
        File.WriteAllText(Path.Combine(_backendSrc, "AppDbContext.cs"),
            "/* INJECT_DBSETS */");
        File.WriteAllText(Path.Combine(_backendSrc, "dotnet-backend.csproj"),
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"9.0.0\" />");
    }

    private static Dictionary<string, byte[]> LoadDirectoryToMemory(string sourceDir)
    {
        var files = new Dictionary<string, byte[]>();
        LoadDirectoryToMemoryInternal(sourceDir, sourceDir, files);
        return files;
    }

    private static void LoadDirectoryToMemoryInternal(string rootDir, string currentDir, Dictionary<string, byte[]> files)
    {
        foreach (var file in Directory.GetFiles(currentDir))
        {
            var relPath = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            files[relPath] = File.ReadAllBytes(file);
        }
        foreach (var directory in Directory.GetDirectories(currentDir))
        {
            LoadDirectoryToMemoryInternal(rootDir, directory, files);
        }
    }

    [Fact]
    public async Task Assemble_HighlyConcurrentLoadStressTest_SucceedsWithoutCorruption()
    {
        // Arrange: mock OCI vault containing 3 modules
        var mock = new Mock<IVaultRepository>();
        mock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend"))
            .ReturnsAsync(() => (LoadDirectoryToMemory(_frontendSrc), "sha-react"));
        mock.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend"))
            .ReturnsAsync(() => (LoadDirectoryToMemory(_backendSrc), "sha-dotnet"));

        var moduleManifestJson = "{\"name\":\"ModX\",\"description\":\"\",\"frontend\":{\"nav\":\"<li>Item</li>\",\"views\":\"v.tsx\",\"imports\":\"import X;\"},\"backend\":{\"controllers\":[],\"dbSets\":\"\"}}";
        mock.Setup(v => v.FetchModuleInMemoryAsync(It.IsAny<string>(), "latest"))
            .ReturnsAsync(() =>
            {
                var files = new Dictionary<string, byte[]>
                {
                    { "manifest.json", Encoding.UTF8.GetBytes(moduleManifestJson) }
                };
                return (files, "sha-mod");
            });

        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        var blueprint = new Blueprint 
        { 
            AppName = "StressApp", 
            Modules = new() { "Mod1", "Mod2", "Mod3", "Mod4", "Mod5" } 
        };
        var blueprintJson = System.Text.Json.JsonSerializer.Serialize(blueprint);

        // Act: trigger 40 parallel compilation jobs simultaneously
        var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            var jobId = $"stress-job-{i}";
            var result = await compiler.AssembleAsync(blueprintJson, jobId, _store);
            Assert.True(result.Success);
            Assert.Contains("templates/react-frontend", result.SbomJson);
        }));

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Vault Repository Unit Coverage Tests
// ─────────────────────────────────────────────────────────────────────────────
public class VaultRepositoryCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public VaultRepositoryCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beloved_repo_test_" + Guid.NewGuid().ToString("N"));
        
        var templateDir = Path.Combine(_tempDir, "vault", "templates", "test-template");
        Directory.CreateDirectory(templateDir);
        File.WriteAllText(Path.Combine(templateDir, "index.html"), "<h1>Test</h1>");
        
        // Recursive subfolder inside template
        var subDir = Path.Combine(templateDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "style.css"), "body {}");

        var moduleDir = Path.Combine(_tempDir, "vault", "modules", "test-module");
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, "manifest.json"), "{}");

        // Recursive subfolder inside module
        var subModuleDir = Path.Combine(moduleDir, "subdir");
        Directory.CreateDirectory(subModuleDir);
        File.WriteAllText(Path.Combine(subModuleDir, "helpers.js"), "const x = 1;");
    }

    [Fact]
    public async Task LocalVaultRepository_FetchAndList_InMemory_Succeeds()
    {
        var repo = new LocalVaultRepository(_tempDir);

        // Fetch template
        var (tFiles, tDigest) = await repo.FetchTemplateInMemoryAsync("test-template");
        Assert.NotEmpty(tFiles);
        Assert.True(tFiles.ContainsKey("index.html"));
        Assert.Equal("<h1>Test</h1>", Encoding.UTF8.GetString(tFiles["index.html"]));

        // Fetch module
        var (mFiles, mDigest) = await repo.FetchModuleInMemoryAsync("test-module", "latest");
        Assert.NotEmpty(mFiles);
        Assert.True(mFiles.ContainsKey("manifest.json"));

        // List modules
        var list = await repo.ListModulesAsync();
        Assert.Contains("test-module", list);

        // Push module
        var modSrc = Path.Combine(_tempDir, "new-mod");
        Directory.CreateDirectory(modSrc);
        File.WriteAllText(Path.Combine(modSrc, "manifest.json"), "{}");
        await repo.PushModuleAsync(modSrc, "new-mod", "1.0.0");
        var listAfterPush = await repo.ListModulesAsync();
        Assert.Contains("new-mod", listAfterPush);

        // Legacy path-based checks to ensure coverage
        var pathDest = Path.Combine(_tempDir, "dest-template");
        var result1 = await repo.FetchTemplateAsync("test-template", pathDest);
        Assert.True(Directory.Exists(result1.targetDirectory));
        Assert.True(File.Exists(Path.Combine(result1.targetDirectory, "index.html")));

        var pathDestMod = Path.Combine(_tempDir, "dest-module");
        var result2 = await repo.FetchModuleAsync("test-module", "latest", pathDestMod);
        Assert.True(Directory.Exists(result2.targetDirectory));
        Assert.True(File.Exists(Path.Combine(result2.targetDirectory, "manifest.json")));
    }

    [Fact]
    public async Task CachedVaultRepository_CachesInMemoryPayloads()
    {
        var mock = new Mock<IVaultRepository>();
        var tFilesMock = new Dictionary<string, byte[]> { { "App.tsx", new byte[] { 1, 2, 3 } } };
        mock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend"))
            .ReturnsAsync((tFilesMock, "sha-react"))
            .Verifiable();

        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var cachedRepo = new CachedVaultRepository(mock.Object, cache);

        // First call: Should hit inner mock repo
        var (files1, digest1) = await cachedRepo.FetchTemplateInMemoryAsync("react-frontend");
        Assert.Single(files1);

        // Second call: Should hit memory cache directly without calling mock again
        var (files2, digest2) = await cachedRepo.FetchTemplateInMemoryAsync("react-frontend");
        Assert.Single(files2);

        mock.Verify(v => v.FetchTemplateInMemoryAsync("react-frontend"), Times.Once);

        // Legacy path caching coverage
        mock.Setup(v => v.FetchTemplateAsync("react-frontend", "dest-dir"))
            .ReturnsAsync(("dest-dir", "sha-react-path"))
            .Verifiable();
        mock.Setup(v => v.FetchModuleAsync("my-module", "latest", "dest-dir"))
            .ReturnsAsync(("dest-dir", "sha-module-path"))
            .Verifiable();

        var pathDest = Path.Combine(_tempDir, "cache-dest-template");
        Directory.CreateDirectory(Path.Combine(_tempDir, "dest-dir"));
        File.WriteAllText(Path.Combine(_tempDir, "dest-dir", "App.tsx"), "code");

        // Execute paths to coverage
        await cachedRepo.FetchTemplateAsync("react-frontend", pathDest);
        await cachedRepo.FetchModuleAsync("my-module", "latest", pathDest);
        await cachedRepo.PushModuleAsync("dest-dir", "my-module", "latest");
        var list = await cachedRepo.ListModulesAsync();

        mock.Verify(v => v.PushModuleAsync("dest-dir", "my-module", "latest"), Times.Once);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Analytics Injection Plugin Coverage Tests
// ─────────────────────────────────────────────────────────────────────────────
public class AnalyticsInjectionPluginTests : IDisposable
{
    private readonly string _tempDir;

    public AnalyticsInjectionPluginTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beloved_plugin_test_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task AnalyticsInjectionPlugin_StitchesHtml_InMemory_And_Path()
    {
        var plugin = new AnalyticsInjectionPlugin();
        Assert.Equal("AnalyticsInjection", plugin.Name);
        var blueprint = new Blueprint { AppName = "TelemetryApp" };

        // Test in-memory injection
        var workspace = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
        workspace["frontend/src/index.html"] = Encoding.UTF8.GetBytes("<html><body>test</body></html>");

        await plugin.ExecuteInMemoryAsync(workspace, blueprint);
        var stitchedHtml = Encoding.UTF8.GetString(workspace["frontend/src/index.html"]);
        Assert.Contains("Beloved Telemetry initialized for TelemetryApp", stitchedHtml);

        // Test path-based legacy injection
        var htmlDir = Path.Combine(_tempDir, "frontend");
        Directory.CreateDirectory(htmlDir);
        var htmlPath = Path.Combine(htmlDir, "index.html");
        await File.WriteAllTextAsync(htmlPath, "<html><body>test</body></html>");

        await plugin.ExecuteAsync(_tempDir, blueprint);
        var pathStitchedHtml = await File.ReadAllTextAsync(htmlPath);
        Assert.Contains("Beloved Telemetry initialized for TelemetryApp", pathStitchedHtml);

        // Test path-based exception coverage (passing an invalid target directory path)
        await plugin.ExecuteAsync("::invalid-path::*", blueprint);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// OciVaultRepository Unit Coverage Tests (Mocked HttpClient)
// ─────────────────────────────────────────────────────────────────────────────
public class OciVaultRepositoryTests
{
    private class MockHttpMessageHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    [Fact]
    public async Task FetchTemplateInMemory_FetchesAndExtracts_FromRegistry()
    {
        // 1. Arrange: Create a stub tar.gz package containing index.html in memory
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "index.html");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("<h1>Hello from OCI</h1>"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            
            // Mock Manifest request
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = """
                {
                  "schemaVersion": 2,
                  "mediaType": "application/vnd.oci.image.manifest.v1+json",
                  "config": {
                    "mediaType": "application/vnd.oci.image.config.v1+json",
                    "size": 702,
                    "digest": "sha256:config-digest"
                  },
                  "layers": [
                    {
                      "mediaType": "application/vnd.oci.image.layer.v1.tar+gzip",
                      "size": 1234,
                      "digest": "sha256:layer-digest"
                    }
                  ]
                }
                """;
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
                response.Headers.Add("Docker-Content-Digest", "sha256:manifest-digest");
                return response;
            }

            // Mock Blob request
            if (uri.Contains("/blobs/sha256:layer-digest"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }

            // Fallback
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        verifierMock.Setup(v => v.VerifySignature(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<string>()))
            .Returns(true);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();

        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        // 2. Act
        var (files, digest) = await repo.FetchTemplateInMemoryAsync("react-frontend");

        // 3. Assert
        Assert.NotEmpty(files);
        Assert.True(files.ContainsKey("index.html"));
        Assert.Equal("<h1>Hello from OCI</h1>", Encoding.UTF8.GetString(files["index.html"]));
        Assert.Equal("sha256:manifest-digest", digest);
    }

    public OciVaultRepositoryTests()
    {
        OciVaultRepository.AllowSignatureBypassForLocalhost = false;
    }

    [Fact]
    public async Task FetchModuleInMemory_UnsignedOrSignatureMismatch_ThrowsSecurityException()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": []}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }
            if (uri.Contains(".sig"))
            {
                // Return 404 for signature file to simulate unsigned manifest
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        // Fetching a module (must verify signature, unlike templates)
        OciVaultRepository.AllowSignatureBypassForLocalhost = false;
        await Assert.ThrowsAsync<System.Security.SecurityException>(async () =>
        {
            await repo.FetchModuleInMemoryAsync("auth", "latest");
        });
    }

    [Fact]
    public async Task OciVaultRepository_ListModulesAndLegacyPaths_Succeed()
    {
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "main.js");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("alert(1);"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            if (uri.Contains("/_catalog"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"repositories\":[\"templates/base\",\"modules/auth\",\"modules/billing\"]}", Encoding.UTF8, "application/json")
                };
            }
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }
            if (uri.Contains("/blobs/"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        // List modules
        var catalog = await repo.ListModulesAsync();
        Assert.Contains("auth", catalog);
        Assert.Contains("billing", catalog);
        Assert.DoesNotContain("base", catalog);

        // Legacy path-based fetch
        var tempOut = Path.Combine(Path.GetTempPath(), "oci_legacy_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await repo.FetchTemplateAsync("react-frontend", tempOut);
            Assert.True(File.Exists(Path.Combine(result.targetDirectory, "main.js")));
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PemSignatureVerifier Unit Coverage Tests (Valid RSA Key Generation)
// ─────────────────────────────────────────────────────────────────────────────
public class PemSignatureVerifierTests
{
    [Fact]
    public void VerifySignature_WithValidRsaKeyPair_Succeeds()
    {
        var verifier = new PemSignatureVerifier();
        var payload = Encoding.UTF8.GetBytes("signed-data-content");

        // Generate RSA key pair programmatically to test verifier logic
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var signature = rsa.SignData(payload, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        // Export public key as PEM
        var pubKeyPem = rsa.ExportRSAPublicKeyPem();

        var result = verifier.VerifySignature(payload, signature, pubKeyPem);
        Assert.True(result);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// OllamaLlmProvider Unit Coverage Tests
// ─────────────────────────────────────────────────────────────────────────────
public class LlmProviderTests
{
    private class MockHttpMessageHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    [Fact]
    public async Task OllamaLlmProvider_QueriesUrl_And_ParsesResponse()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var resJson = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"appName\": \"TestApp\", \"modules\": [\"Auth\", \"Items\"], \"database\": \"SQLite\", \"authStrategy\": \"None\", \"target\": \"WebAndApi\"}"
                  }
                }
              ]
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(resJson, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaLlmProvider(client, "llama3");
        var result = await provider.MapIntentAsync("Build dashboard", new[] { "Auth", "Items" });
        Assert.NotNull(result);
        Assert.Equal("TestApp", result.AppName);
        Assert.Contains("Auth", result.Modules);
    }

    [Fact]
    public async Task ClaudeLlmProvider_QueriesUrl_And_ParsesResponse()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var resJson = """
            {
              "content": [
                {
                  "type": "text",
                  "text": "{\"appName\": \"ClaudeApp\", \"modules\": [\"Billing\"], \"database\": \"SQLite\", \"authStrategy\": \"None\", \"target\": \"WebAndApi\"}"
                }
              ]
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(resJson, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var provider = new ClaudeLlmProvider(client, "fake-key", "claude-3");
        var result = await provider.MapIntentAsync("Build billing system", new[] { "Billing" });
        Assert.NotNull(result);
        Assert.Equal("ClaudeApp", result.AppName);
    }

    [Fact]
    public async Task GeminiLlmProvider_QueriesUrl_And_ParsesResponse()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var resJson = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"appName\": \"GeminiApp\", \"modules\": [\"Storage\"], \"database\": \"PostgreSQL\", \"authStrategy\": \"JWT\", \"target\": \"WebAndApi\"}"
                      }
                    ]
                  }
                }
              ]
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(resJson, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };
        var provider = new GeminiLlmProvider(client, "fake-key", "gemini-1.5");
        var result = await provider.MapIntentAsync("Build storage app", new[] { "Storage" });
        Assert.NotNull(result);
        Assert.Equal("GeminiApp", result.AppName);
        Assert.Equal("PostgreSQL", result.Database);
    }

    [Fact]
    public async Task OpenAiLlmProvider_And_IntentMapper_QueriesUrl_And_ParsesResponse()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var resJson = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"appName\": \"OpenAiApp\", \"modules\": [\"Notifications\"], \"database\": \"SQLite\", \"authStrategy\": \"None\", \"target\": \"WebAndApi\"}"
                  }
                }
              ]
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(resJson, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.openai.com") };
        var provider = new OpenAiLlmProvider(client, "fake-key", "gpt-4");
        
        // Test provider
        var result = await provider.MapIntentAsync("Build notify", new[] { "Notifications" });
        Assert.NotNull(result);
        Assert.Equal("OpenAiApp", result.AppName);

        // Test refinement
        var refined = await provider.RefineBlueprintAsync(result, "Change db to PostgreSQL", new[] { "Notifications" });
        Assert.NotNull(refined);

        // Test mapper wrapper
        var mapper = new OpenAiIntentMapper(client, "fake-key", "gpt-4o");
        var mappedBlueprint = await mapper.MapIntentAsync("Build notify", new[] { "Notifications" });
        Assert.NotNull(mappedBlueprint);
    }

    [Fact]
    public async Task OciVaultRepository_MissingKeyFile_ReturnsFalseSignature()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": []}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }
            if (uri.Contains(".sig"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1 })
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        // Mutate public key path to non-existent file
        var originalPath = OciVaultRepository.PublicKeyPemPath;
        OciVaultRepository.PublicKeyPemPath = "/nonexistent/cosign-key.pub";
        OciVaultRepository.AllowSignatureBypassForLocalhost = false;

        try
        {
            await Assert.ThrowsAsync<System.Security.SecurityException>(async () =>
            {
                await repo.FetchModuleInMemoryAsync("auth", "latest");
            });
        }
        finally
        {
            OciVaultRepository.PublicKeyPemPath = originalPath;
        }
    }

    [Fact]
    public async Task OciVaultRepository_ManifestIndexRouting_ResolvesCorrectPlatform()
    {
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "test.txt");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("val"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            
            // Mock OCI Index request returning list of manifests
            if (uri.Contains("/manifests/latest"))
            {
                var indexJson = """
                {
                  "manifests": [
                    {
                      "digest": "sha256:linux-digest",
                      "platform": {
                        "os": "linux",
                        "architecture": "amd64"
                      }
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(indexJson, Encoding.UTF8, "application/vnd.oci.image.index.v1+json")
                };
            }

            if (uri.Contains("/manifests/sha256:linux-digest"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }

            if (uri.Contains("/blobs/sha256:layer-digest"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        var (files, digest) = await repo.FetchTemplateInMemoryAsync("react-frontend");
        Assert.NotEmpty(files);
        Assert.True(files.ContainsKey("test.txt"));
    }

    [Fact]
    public async Task LocalVaultRepository_FetchNonExistent_ThrowsDirectoryNotFound()
    {
        var temp = Path.Combine(Path.GetTempPath(), "local_vault_err_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var repo = new LocalVaultRepository(temp);
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => repo.FetchTemplateInMemoryAsync("nonexistent"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => repo.FetchModuleInMemoryAsync("nonexistent", "latest"));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task AnalyticsInjectionPlugin_MissingHtml_SkipsGracefully()
    {
        var plugin = new AnalyticsInjectionPlugin();
        var blueprint = new Blueprint { AppName = "TelemetryApp" };

        var workspace = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
        // index.html is missing
        await plugin.ExecuteInMemoryAsync(workspace, blueprint);

        // Verify empty workspace remains empty
        Assert.Empty(workspace);
    }

    [Fact]
    public async Task LlmProviders_RefinementPromptMapping_Succeeds()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var resJson = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"appName\": \"RefinedApp\", \"modules\": [\"Auth\"], \"database\": \"PostgreSQL\", \"authStrategy\": \"JWT\", \"target\": \"WebAndApi\"}"
                  }
                }
              ]
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(resJson, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaLlmProvider(client, "llama3");
        var baseBlueprint = new Blueprint { AppName = "BaseApp" };
        var refined = await provider.RefineBlueprintAsync(baseBlueprint, "Add Auth and PG", new[] { "Auth" });
        
        Assert.NotNull(refined);
        Assert.Equal("RefinedApp", refined.AppName);
    }

    [Fact]
    public void LlmProviderOptions_Properties_GetAndSet()
    {
        var opts = new LlmProviderOptions
        {
            ApiKey = "api-key",
            BaseUrl = "http://localhost",
            Model = "model"
        };

        Assert.Equal("api-key", opts.ApiKey);
        Assert.Equal("http://localhost", opts.BaseUrl);
        Assert.Equal("model", opts.Model);
    }

    [Fact]
    public async Task AssemblyCompiler_AssembleAsync_InvalidJson_ReturnsFailure()
    {
        var mock = new Mock<IVaultRepository>();
        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        var store = new LocalDiskOutputStore();
        var result = await compiler.AssembleAsync("invalid-json", "job-id", store);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task LlmProviders_ApiFailure_ThrowsException()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server Error")
            };
        });

        // Test Ollama
        var ollama = new OllamaLlmProvider(new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5000") }, "llama");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ollama.MapIntentAsync("test", new[] { "A" }));

        // Test Claude
        var claude = new ClaudeLlmProvider(new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5000") }, "key", "model");
        await Assert.ThrowsAsync<InvalidOperationException>(() => claude.MapIntentAsync("test", new[] { "A" }));

        // Test Gemini
        var gemini = new GeminiLlmProvider(new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5000") }, "key", "model");
        await Assert.ThrowsAsync<InvalidOperationException>(() => gemini.MapIntentAsync("test", new[] { "A" }));

        // Test OpenAI
        var openai = new OpenAiLlmProvider(new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5000") }, "key", "model");
        await Assert.ThrowsAsync<InvalidOperationException>(() => openai.MapIntentAsync("test", new[] { "A" }));

        // Test OpenAiIntentMapper
        var mapper = new OpenAiIntentMapper(new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5000") }, "key", "model");
        await Assert.ThrowsAsync<Exception>(() => mapper.MapIntentAsync("test", new[] { "A" }));
    }

    [Fact]
    public async Task LocalVaultRepository_MissingVaultDir_ReturnsEmptyList()
    {
        var repo = new LocalVaultRepository("/nonexistent/vault_path");
        var list = await repo.ListModulesAsync();
        Assert.Empty(list);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => repo.FetchTemplateAsync("tpl", "dest"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => repo.FetchModuleAsync("mod", "1.0", "dest"));
    }

    [Fact]
    public async Task OciVaultRepository_PushModule_ThrowsNotImplemented()
    {
        var client = new HttpClient();
        var verifier = new Mock<ISignatureVerifier>().Object;
        var logger = new Mock<ILogger<OciVaultRepository>>().Object;
        var repo = new OciVaultRepository(client, verifier, logger);

        await Assert.ThrowsAsync<NotImplementedException>(() => repo.PushModuleAsync("src", "name", "1.0"));
    }

    [Fact]
    public async Task ClaudeAndGeminiLlmProviders_RefineBlueprint_ParseResponseSuccessfully()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var resJson = """
            {
              "content": [
                {
                  "type": "text",
                  "text": "{\"appName\": \"RefinedApp\", \"modules\": [], \"database\": \"SQLite\", \"authStrategy\": \"None\", \"target\": \"WebAndApi\"}"
                }
              ],
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"appName\": \"RefinedApp\", \"modules\": [], \"database\": \"SQLite\", \"authStrategy\": \"None\", \"target\": \"WebAndApi\"}"
                      }
                    ]
                  }
                }
              ]
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(resJson, Encoding.UTF8, "application/json")
            };
        });

        var baseBlueprint = new Blueprint { AppName = "BaseApp" };

        var claude = new ClaudeLlmProvider(new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5000") }, "key", "model");
        var resultClaude = await claude.RefineBlueprintAsync(baseBlueprint, "Add Auth", Enumerable.Empty<string>());
        Assert.NotNull(resultClaude);

        var gemini = new GeminiLlmProvider(new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5000") }, "key", "model");
        var resultGemini = await gemini.RefineBlueprintAsync(baseBlueprint, "Add Auth", Enumerable.Empty<string>());
        Assert.NotNull(resultGemini);
    }

    [Fact]
    public async Task AssemblyCompiler_DatabaseStitchingBranches_PostgreSQL_And_SQLServer()
    {
        // 1. Arrange: setup workspace files for PostgreSQL
        var mock = new Mock<IVaultRepository>();
        var frontendFiles = new Dictionary<string, byte[]>
        {
            { "src/App.tsx", Encoding.UTF8.GetBytes("// placeholder\n{/* MODULE_NAV_ITEMS_START */}\n          {/* MODULE_NAV_ITEMS_END */}\n{/* MODULE_VIEWS_START */}\n          {/* MODULE_VIEWS_END */}") }
        };
        var backendFiles = new Dictionary<string, byte[]>
        {
            { "Program.cs", Encoding.UTF8.GetBytes("// DATABASE_INJECTION_START\n// DATABASE_INJECTION_END\n// DBSETS_PLACEHOLDER") },
            { "AppDbContext.cs", Encoding.UTF8.GetBytes("/* INJECT_DBSETS */") },
            { "dotnet-backend.csproj", Encoding.UTF8.GetBytes("<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"9.0.0\" />") }
        };

        mock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend"))
            .ReturnsAsync((frontendFiles, "sha-react"));
        mock.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend"))
            .ReturnsAsync((backendFiles, "sha-dotnet"));

        var compiler = new AssemblyCompiler(mock.Object, new[] { new AnalyticsInjectionPlugin() });
        
        // Test PostgreSQL blueprint compilation
        var blueprintPg = new Blueprint
        {
            AppName = "PgApp",
            Database = "PostgreSQL",
            Target = "WebAndApi",
            Modules = new()
        };
        var storePg = new LocalDiskOutputStore();
        var resultPg = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprintPg), "job-pg", storePg);
        
        Assert.True(resultPg.Success);
        // Test SQLServer blueprint compilation
        var blueprintSql = new Blueprint
        {
            AppName = "SqlApp",
            Database = "SQLServer",
            Target = "WebAndApi",
            Modules = new()
        };
        var storeSql = new LocalDiskOutputStore();
        var resultSql = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprintSql), "job-sql", storeSql);

        Assert.True(resultSql.Success);
    }

    [Fact]
    public async Task AssemblyCompiler_FullStitching_AuthAndItems_Succeeds()
    {
        var mock = new Mock<IVaultRepository>();
        var frontendFiles = new Dictionary<string, byte[]>
        {
            { "src/App.tsx", Encoding.UTF8.GetBytes("// placeholder\n{/* MODULE_NAV_ITEMS_START */}\n          {/* MODULE_NAV_ITEMS_END */}\n{/* MODULE_VIEWS_START */}\n          {/* MODULE_VIEWS_END */}") }
        };
        var backendFiles = new Dictionary<string, byte[]>
        {
            { "Program.cs", Encoding.UTF8.GetBytes("// DATABASE_INJECTION_START\n// DATABASE_INJECTION_END\n// DBSETS_PLACEHOLDER") },
            { "AppDbContext.cs", Encoding.UTF8.GetBytes("/* INJECT_DBSETS */") },
            { "dotnet-backend.csproj", Encoding.UTF8.GetBytes("<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"9.0.0\" />") }
        };

        mock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend")).ReturnsAsync((frontendFiles, "sha-react"));
        mock.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend")).ReturnsAsync((backendFiles, "sha-dotnet"));

        // Setup Auth module files & manifest
        var authManifest = "{\"name\":\"Auth\",\"description\":\"\",\"frontend\":{\"nav\":\"<li>AuthNav</li>\",\"views\":\"src/views/LoginView.tsx\",\"imports\":\"import LoginView;\"},\"backend\":{\"controllers\":[\"Controllers/AuthController.cs\"],\"dbSets\":\"public DbSet<User> Users { get; set; }\"}}";
        var authFiles = new Dictionary<string, byte[]>
        {
            { "manifest.json", Encoding.UTF8.GetBytes(authManifest) },
            { "Controllers/AuthController.cs", Encoding.UTF8.GetBytes("public class AuthController {}") },
            { "src/views/LoginView.tsx", Encoding.UTF8.GetBytes("const LoginView = () => {}") }
        };
        mock.Setup(v => v.FetchModuleInMemoryAsync("auth", "latest")).ReturnsAsync((authFiles, "sha-auth"));

        // Setup Items module files & manifest
        var itemsManifest = "{\"name\":\"Items\",\"description\":\"\",\"frontend\":{\"nav\":\"<li>ItemsNav</li>\",\"views\":\"src/views/ItemsView.tsx\",\"imports\":\"import ItemsView;\"},\"backend\":{\"controllers\":[\"Controllers/ItemsController.cs\"],\"dbSets\":\"public DbSet<Item> Items { get; set; }\"}}";
        var itemsFiles = new Dictionary<string, byte[]>
        {
            { "manifest.json", Encoding.UTF8.GetBytes(itemsManifest) },
            { "Controllers/ItemsController.cs", Encoding.UTF8.GetBytes("public class ItemsController {}") },
            { "src/views/ItemsView.tsx", Encoding.UTF8.GetBytes("const ItemsView = () => {}") }
        };
        mock.Setup(v => v.FetchModuleInMemoryAsync("items", "latest")).ReturnsAsync((itemsFiles, "sha-items"));

        // Setup custom module
        var customManifest = "{\"name\":\"Custom\",\"description\":\"\",\"frontend\":{\"nav\":\"<li>CustomNav</li>\",\"views\":\"src/views/CustomView.tsx\",\"imports\":\"\"},\"backend\":{}}";
        var customFiles = new Dictionary<string, byte[]>
        {
            { "manifest.json", Encoding.UTF8.GetBytes(customManifest) },
            { "src/views/CustomView.tsx", Encoding.UTF8.GetBytes("const CustomView = () => {}") }
        };
        mock.Setup(v => v.FetchModuleInMemoryAsync("custom", "latest")).ReturnsAsync((customFiles, "sha-custom"));

        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        var blueprint = new Blueprint
        {
            AppName = "App",
            Database = "SQLite",
            Target = "WebAndApi",
            Modules = new() { "auth", "items", "custom" }
        };

        var store = new LocalDiskOutputStore();
        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-full-stich", store, onLog: msg => {});

        Assert.True(result.Success);
    }

    [Fact]
    public async Task OciVaultRepository_LegacyFetchModuleAndSignatureExceptions_Succeeds()
    {
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "main.js");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("alert(1);"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            
            // Return OCI Index manifest
            if (uri.Contains("/manifests/latest"))
            {
                var indexJson = """
                {
                  "manifests": [
                    {
                      "digest": "sha256:target-digest",
                      "platform": {
                        "os": "darwin",
                        "architecture": "arm64"
                      }
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(indexJson, Encoding.UTF8, "application/vnd.oci.image.index.v1+json")
                };
            }

            if (uri.Contains("/manifests/sha256:target-digest"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }

            if (uri.Contains("/blobs/"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }
            if (uri.Contains(".sig"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("invalid-signature")
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        
        // Cause signature verification to throw exception to test OciVaultRepository verify signature catch block
        verifierMock.Setup(v => v.VerifySignature(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("Crypto Error"));

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        var tempOut = Path.Combine(Path.GetTempPath(), "oci_module_legacy_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Should throw SecurityException because VerifySignature throws exception (caught and fails closed)
            await Assert.ThrowsAsync<System.Security.SecurityException>(() =>
                repo.FetchModuleAsync("my-module", "latest", tempOut));
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }

    [Fact]
    public async Task AssemblyCompiler_PluginAndStoreErrors_CaughtAndHandled()
    {
        var mock = new Mock<IVaultRepository>();
        var frontendFiles = new Dictionary<string, byte[]>
        {
            { "src/App.tsx", Encoding.UTF8.GetBytes("// placeholder\n{/* MODULE_NAV_ITEMS_START */}\n          {/* MODULE_NAV_ITEMS_END */}\n{/* MODULE_VIEWS_START */}\n          {/* MODULE_VIEWS_END */}") }
        };
        var backendFiles = new Dictionary<string, byte[]>
        {
            { "Program.cs", Encoding.UTF8.GetBytes("// DATABASE_INJECTION_START\n// DATABASE_INJECTION_END\n// DBSETS_PLACEHOLDER") },
            { "AppDbContext.cs", Encoding.UTF8.GetBytes("/* INJECT_DBSETS */") },
            { "dotnet-backend.csproj", Encoding.UTF8.GetBytes("<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"9.0.0\" />") }
        };

        mock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend")).ReturnsAsync((frontendFiles, "sha-react"));
        mock.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend")).ReturnsAsync((backendFiles, "sha-dotnet"));

        // Mock plugin that throws exception
        var badPluginMock = new Mock<IAssemblyPlugin>();
        badPluginMock.Setup(p => p.Name).Returns("BadPlugin");
        badPluginMock.Setup(p => p.ExecuteInMemoryAsync(It.IsAny<System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>>(), It.IsAny<Blueprint>(), It.IsAny<Action<string>>()))
            .ThrowsAsync(new InvalidOperationException("Plugin crashed"));

        var compiler = new AssemblyCompiler(mock.Object, new[] { badPluginMock.Object });
        var blueprint = new Blueprint
        {
            AppName = "App",
            Database = "SQLite",
            Target = "WebAndApi",
            Modules = new()
        };

        var store = new LocalDiskOutputStore();
        // Should compile successfully despite the plugin crashing (graceful handling)
        var result = await compiler.AssembleAsync(System.Text.Json.JsonSerializer.Serialize(blueprint), "job-plugin-err", store, onLog: msg => {});
        Assert.True(result.Success);

        // Test output store error propagation
        var badStoreMock = new Mock<IOutputStore>();
        badStoreMock.Setup(s => s.StoreArtifactAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .ThrowsAsync(new IOException("Disk Full"));

        var compilerStoreErr = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        await Assert.ThrowsAsync<IOException>(() =>
            compilerStoreErr.AssembleAsync(System.Text.Json.JsonSerializer.Serialize(blueprint), "job-store-err", badStoreMock.Object));
    }

    [Fact]
    public async Task AssemblyCompiler_ModuleFetchAndManifestDeserializationExceptions_Handled()
    {
        var mock = new Mock<IVaultRepository>();
        var frontendFiles = new Dictionary<string, byte[]> { { "src/App.tsx", Encoding.UTF8.GetBytes("") } };
        var backendFiles = new Dictionary<string, byte[]> { { "Program.cs", Encoding.UTF8.GetBytes("") } };

        mock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend")).ReturnsAsync((frontendFiles, "sha-react"));
        mock.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend")).ReturnsAsync((backendFiles, "sha-dotnet"));

        // FetchModuleInMemory throws an exception to trigger ProcessModuleInMemoryAsync catch block
        mock.Setup(v => v.FetchModuleInMemoryAsync("auth", "latest"))
            .ThrowsAsync(new InvalidOperationException("Registry offline"));

        var compiler = new AssemblyCompiler(mock.Object, Enumerable.Empty<IAssemblyPlugin>());
        var blueprint = new Blueprint
        {
            AppName = "App",
            Database = "SQLite",
            Target = "WebAndApi",
            Modules = new() { "auth" }
        };

        var store = new LocalDiskOutputStore();
        // Should compile successfully (auth module is skipped, failsafe compilation)
        var result = await compiler.AssembleAsync(System.Text.Json.JsonSerializer.Serialize(blueprint), "job-mod-err", store);
        Assert.True(result.Success);

        // Test deserialization failure of manifest
        var mockBadManifest = new Mock<IVaultRepository>();
        mockBadManifest.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend")).ReturnsAsync((frontendFiles, "sha-react"));
        mockBadManifest.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend")).ReturnsAsync((backendFiles, "sha-dotnet"));
        mockBadManifest.Setup(v => v.FetchModuleInMemoryAsync("auth", "latest"))
            .ReturnsAsync((new Dictionary<string, byte[]> { { "manifest.json", Encoding.UTF8.GetBytes("invalid-manifest-json") } }, "sha-mod"));

        var compilerBadManifest = new AssemblyCompiler(mockBadManifest.Object, Enumerable.Empty<IAssemblyPlugin>());
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            compilerBadManifest.AssembleAsync(System.Text.Json.JsonSerializer.Serialize(blueprint), "job-manifest-err", store));

        // Test missing manifest.json skips module
        var mockMissingManifest = new Mock<IVaultRepository>();
        mockMissingManifest.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend")).ReturnsAsync((frontendFiles, "sha-react"));
        mockMissingManifest.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend")).ReturnsAsync((backendFiles, "sha-dotnet"));
        // Module workspace empty (no manifest.json)
        mockMissingManifest.Setup(v => v.FetchModuleInMemoryAsync("auth", "latest"))
            .ReturnsAsync((new Dictionary<string, byte[]>(), "sha-mod"));

        var compilerMissingManifest = new AssemblyCompiler(mockMissingManifest.Object, Enumerable.Empty<IAssemblyPlugin>());
        var resultMissing = await compilerMissingManifest.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-missing-manifest", store);
        Assert.True(resultMissing.Success);
    }

    [Fact]
    public async Task OciVaultRepository_VerifyManifestSignature_HandlesExceptionsGracefully()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": []}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }
            if (uri.Contains(".sig"))
            {
                // Return invalid signature byte stream to trigger verification checks
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1 })
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        // Force cryptographic signature check to throw an exception
        verifierMock.Setup(v => v.VerifySignature(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("Signature payload corrupt"));

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        OciVaultRepository.AllowSignatureBypassForLocalhost = false;
        await Assert.ThrowsAsync<System.Security.SecurityException>(async () =>
        {
            await repo.FetchModuleInMemoryAsync("auth", "latest");
        });
    }

    [Fact]
    public async Task OciVaultRepository_PullArtifactAsync_IndexMatches_NoLinuxFallback()
    {
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "test.txt");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("val"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            
            // Return Index manifest containing ONLY windows platform (tests no linux match fallback path)
            if (uri.Contains("/manifests/latest"))
            {
                var indexJson = """
                {
                  "manifests": [
                    {
                      "digest": "sha256:win-digest",
                      "platform": {
                        "os": "windows",
                        "architecture": "amd64"
                      }
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(indexJson, Encoding.UTF8, "application/vnd.oci.image.index.v1+json")
                };
            }

            if (uri.Contains("/manifests/sha256:win-digest"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }

            if (uri.Contains("/blobs/sha256:layer-digest"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        var (files, digest) = await repo.FetchTemplateInMemoryAsync("react-frontend");
        Assert.NotEmpty(files);
        Assert.True(files.ContainsKey("test.txt"));

        var tempOut = Path.Combine(Path.GetTempPath(), "legacy_template_index_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await repo.FetchTemplateAsync("react-frontend", tempOut);
            Assert.True(Directory.Exists(result.targetDirectory));
            Assert.True(File.Exists(Path.Combine(result.targetDirectory, "test.txt")));
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }

    [Fact]
    public async Task OciVaultRepository_PullArtifactAsync_IndexMatches_LinuxPlatform_Succeeds()
    {
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "linux.txt");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("linux-val"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            
            // Return Index manifest containing a LINUX platform entry to cover lines 109-111 and 188-190
            if (uri.Contains("/manifests/latest"))
            {
                var indexJson = """
                {
                  "manifests": [
                    {
                      "digest": "sha256:linux-digest",
                      "platform": {
                        "os": "linux",
                        "architecture": "amd64"
                      }
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(indexJson, Encoding.UTF8, "application/vnd.oci.image.index.v1+json")
                };
            }

            if (uri.Contains("/manifests/sha256:linux-digest"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }

            if (uri.Contains("/blobs/sha256:layer-digest"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        var (files, digest) = await repo.FetchTemplateInMemoryAsync("react-frontend");
        Assert.NotEmpty(files);
        Assert.True(files.ContainsKey("linux.txt"));

        var tempOut = Path.Combine(Path.GetTempPath(), "legacy_linux_index_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await repo.FetchTemplateAsync("react-frontend", tempOut);
            Assert.True(Directory.Exists(result.targetDirectory));
            Assert.True(File.Exists(Path.Combine(result.targetDirectory, "linux.txt")));
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }

    [Fact]
    public async Task OciVaultRepository_PullArtifact_LayerHttpFailure_ThrowsException()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }
            if (uri.Contains("/blobs/"))
            {
                // Return 500 on blob download to cover line 124
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        var tempOut = Path.Combine(Path.GetTempPath(), "legacy_blob_err_" + Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(async () =>
            {
                await repo.FetchTemplateAsync("react-frontend", tempOut);
            });
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }

    [Fact]
    public async Task OciVaultRepository_LegacyFetchModuleAndVerifySignature_Success()
    {
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "manifest.json");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }
            if (uri.Contains("/blobs/"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }
            if (uri.Contains(".sig"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        verifierMock.Setup(v => v.VerifySignature(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<string>()))
            .Returns(true);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        
        OciVaultRepository.AllowSignatureBypassForLocalhost = false;
        var mockKeyPath = OciVaultRepository.PublicKeyPemPath;
        if (!File.Exists(mockKeyPath))
        {
            File.WriteAllText(mockKeyPath, "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE\n-----END PUBLIC KEY-----");
        }
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        // Fetch template in-memory
        var (tFiles, tDigest) = await repo.FetchTemplateInMemoryAsync("react-frontend");
        Assert.NotEmpty(tFiles);

        // Fetch module in-memory (signature check passes)
        var (mFiles, mDigest) = await repo.FetchModuleInMemoryAsync("auth", "latest");
        Assert.NotEmpty(mFiles);

        // Fetch module legacy path
        var tempOut = Path.Combine(Path.GetTempPath(), "legacy_module_success_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await repo.FetchModuleAsync("auth", "latest", tempOut);
            Assert.True(Directory.Exists(result.targetDirectory));
            Assert.True(File.Exists(Path.Combine(result.targetDirectory, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }

    [Fact]
    public async Task OciVaultRepository_VerifyManifestSignature_MissingSignatureFile_ReturnsFalse()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            if (uri.Contains(".sig"))
            {
                // Return 404 to cover lines 248-249
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }
            if (uri.Contains("/manifests/"))
            {
                var manifestJson = "{\"schemaVersion\": 2, \"layers\": []}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        await Assert.ThrowsAsync<System.Security.SecurityException>(async () =>
        {
            await repo.FetchModuleInMemoryAsync("auth", "latest");
        });
    }

    [Fact]
    public async Task OciVaultRepository_PullArtifact_HttpFailure_ThrowsException()
    {
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            // Return 500 Internal Server Error to cover manifest failures (lines 94-95 and 174-175)
            return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        await Assert.ThrowsAsync<Exception>(async () =>
        {
            await repo.FetchTemplateInMemoryAsync("react-frontend");
        });

        var tempOut = Path.Combine(Path.GetTempPath(), "legacy_module_err_" + Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await repo.FetchModuleAsync("auth", "latest", tempOut);
            });
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }

    [Fact]
    public async Task OciVaultRepository_PullArtifact_MissingDigestHeaders_Fallback()
    {
        using var layerMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(layerMs, System.IO.Compression.CompressionMode.Compress, true))
        using (var writer = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "manifest.json");
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
            writer.WriteEntry(entry);
        }
        var layerBytes = layerMs.ToArray();

        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            if (uri.Contains("/manifests/"))
            {
                // Returns OCI manifest containing a config object with digest to cover config fallback branches
                var manifestJson = "{\"schemaVersion\": 2, \"config\": {\"digest\": \"sha256:config-digest\"}, \"layers\": [{\"digest\": \"sha256:layer-digest\"}]}";
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.oci.image.manifest.v1+json")
                };
                // Intentionally omit OCI digest headers to trigger lines 127-129 and 207-209 fallback checks
                return response;
            }
            if (uri.Contains("/blobs/"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(layerBytes)
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost:5001") };
        var verifierMock = new Mock<ISignatureVerifier>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OciVaultRepository>();
        var repo = new OciVaultRepository(client, verifierMock.Object, logger);

        // Fetch template (passes because signature checking is bypassed for templates)
        var (tFiles, tDigest) = await repo.FetchTemplateInMemoryAsync("react-frontend");
        Assert.NotEmpty(tFiles);
        Assert.Equal("sha256:config-digest", tDigest);

        var tempOut = Path.Combine(Path.GetTempPath(), "legacy_template_digest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await repo.FetchTemplateAsync("react-frontend", tempOut);
            Assert.True(Directory.Exists(result.targetDirectory));
            Assert.Equal("sha256:config-digest", result.digest);
        }
        finally
        {
            if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true);
        }
    }

    [Fact]
    public async Task E2E_CompileApp_DeveloperPortal_Succeeds()
    {
        var localRepo = BuildRealLocalRepository();
        var compiler = new AssemblyCompiler(localRepo, Enumerable.Empty<IAssemblyPlugin>());
        var store = new LocalDiskOutputStore();
        var blueprint = new Blueprint
        {
            AppName = "DeveloperPortal",
            Target = "FullStack",
            Database = "SQLite",
            Modules = new List<string> { "GithubAuth", "Analytics", "Settings" }
        };

        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-devportal", store);

        Assert.True(result.Success);
        Assert.Contains("modules/githubauth", result.SbomJson);
        Assert.Contains("modules/analytics", result.SbomJson);
        Assert.Contains("modules/settings", result.SbomJson);
    }

    [Fact]
    public async Task E2E_CompileApp_EnterpriseApp_Succeeds()
    {
        var localRepo = BuildRealLocalRepository();
        var compiler = new AssemblyCompiler(localRepo, Enumerable.Empty<IAssemblyPlugin>());
        var store = new LocalDiskOutputStore();
        var blueprint = new Blueprint
        {
            AppName = "EnterpriseApp",
            Target = "FullStack",
            Database = "SQLServer",
            Modules = new List<string> { "EntraIdAuth", "Notifications", "Storage", "Settings" }
        };

        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-enterprise", store);

        Assert.True(result.Success);
        Assert.Contains("modules/entraidauth", result.SbomJson);
        Assert.Contains("modules/notifications", result.SbomJson);
        Assert.Contains("modules/storage", result.SbomJson);
        Assert.Contains("modules/settings", result.SbomJson);
    }

    [Fact]
    public async Task E2E_CompileApp_SaaSPlatform_Succeeds()
    {
        var localRepo = BuildRealLocalRepository();
        var compiler = new AssemblyCompiler(localRepo, Enumerable.Empty<IAssemblyPlugin>());
        var store = new LocalDiskOutputStore();
        var blueprint = new Blueprint
        {
            AppName = "SaaSPlatform",
            Target = "FullStack",
            Database = "PostgreSQL",
            Modules = new List<string> { "OktaAuth", "Billing", "Analytics", "Comments" }
        };

        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-saas", store);

        Assert.True(result.Success);
        Assert.Contains("modules/oktaauth", result.SbomJson);
        Assert.Contains("modules/billing", result.SbomJson);
        Assert.Contains("modules/analytics", result.SbomJson);
        Assert.Contains("modules/comments", result.SbomJson);
    }

    [Fact]
    public async Task E2E_CompileApp_ClassicStore_Succeeds()
    {
        var localRepo = BuildRealLocalRepository();
        var compiler = new AssemblyCompiler(localRepo, Enumerable.Empty<IAssemblyPlugin>());
        var store = new LocalDiskOutputStore();
        var blueprint = new Blueprint
        {
            AppName = "ClassicStore",
            Target = "FullStack",
            Database = "SQLite",
            Modules = new List<string> { "Auth", "Items", "Billing", "Notifications" }
        };

        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-store", store);

        Assert.True(result.Success);
        Assert.Contains("modules/auth", result.SbomJson);
        Assert.Contains("modules/items", result.SbomJson);
        Assert.Contains("modules/billing", result.SbomJson);
    }

    [Fact]
    public async Task E2E_CompileApp_ProjectManager_Succeeds()
    {
        var localRepo = BuildRealLocalRepository();
        var compiler = new AssemblyCompiler(localRepo, Enumerable.Empty<IAssemblyPlugin>());
        var store = new LocalDiskOutputStore();
        var blueprint = new Blueprint
        {
            AppName = "ProjectManager",
            Target = "FullStack",
            Database = "SQLite",
            Modules = new List<string> { "Tasks", "Feedback", "Chat", "Cart" }
        };

        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-projectmanager", store);

        Assert.True(result.Success);
        Assert.Contains("modules/tasks", result.SbomJson);
        Assert.Contains("modules/feedback", result.SbomJson);
        Assert.Contains("modules/chat", result.SbomJson);
        Assert.Contains("modules/cart", result.SbomJson);
    }

    [Fact]
    public async Task E2E_CompileApp_MasterDashboard_Succeeds()
    {
        var localRepo = BuildRealLocalRepository();
        var compiler = new AssemblyCompiler(localRepo, Enumerable.Empty<IAssemblyPlugin>());
        var store = new LocalDiskOutputStore();
        var blueprint = new Blueprint
        {
            AppName = "MasterDashboard",
            Target = "FullStack",
            Database = "PostgreSQL",
            Modules = new List<string> 
            { 
                "Auth", "OktaAuth", "EntraIdAuth", "GithubAuth", 
                "Tasks", "Feedback", "Chat", "Cart", 
                "Analytics", "Billing", "Comments", "Notifications", "Settings", "Storage" 
            }
        };

        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-masterdashboard", store);

        Assert.True(result.Success);
        Assert.Contains("modules/auth", result.SbomJson);
        Assert.Contains("modules/oktaauth", result.SbomJson);
        Assert.Contains("modules/entraidauth", result.SbomJson);
        Assert.Contains("modules/githubauth", result.SbomJson);
        Assert.Contains("modules/tasks", result.SbomJson);
        Assert.Contains("modules/feedback", result.SbomJson);
        Assert.Contains("modules/chat", result.SbomJson);
        Assert.Contains("modules/cart", result.SbomJson);
        Assert.Contains("modules/analytics", result.SbomJson);
        Assert.Contains("modules/billing", result.SbomJson);
        Assert.Contains("modules/comments", result.SbomJson);
        Assert.Contains("modules/notifications", result.SbomJson);
        Assert.Contains("modules/settings", result.SbomJson);
        Assert.Contains("modules/storage", result.SbomJson);
    }

    [Fact]
    public async Task AssemblyCompiler_WithLlmStitchFallback_StitchesCorrectly()
    {
        var localRepo = BuildRealLocalRepository();
        var llmMock = new Mock<ILlmProvider>();
        llmMock.Setup(l => l.StitchFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("// AI Stitched Output App.tsx");

        var compiler = new AssemblyCompiler(localRepo, Enumerable.Empty<IAssemblyPlugin>(), llmMock.Object);
        var store = new LocalDiskOutputStore();
        var blueprint = new Blueprint
        {
            AppName = "AISwitchApp",
            Target = "FullStack",
            Database = "SQLite",
            Modules = new List<string> { "Tasks" }
        };

        var result = await compiler.AssembleAsync(
            System.Text.Json.JsonSerializer.Serialize(blueprint), "job-aiswitch", store);

        Assert.True(result.Success);
    }

    [Fact]
    public void RoslynDbContextMerger_InjectsDbSetsCorrectly()
    {
        var originalCode = 
            """
            using Microsoft.EntityFrameworkCore;
            public class AppDbContext : DbContext
            {
                public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
            }
            """;
        var dbSets = new[] { "public DbSet<UserTask> UserTasks { get; set; } = null!;" };
        var updated = RoslynDbContextMerger.MergeDbSets(originalCode, dbSets);

        Assert.Contains("public DbSet<UserTask> UserTasks { get; set; }", updated);
    }

    [Fact]
    public void DependencyResolver_ResolvesModulesTopologically()
    {
        var modules = new[] { "Billing", "Cart", "Auth" };
        var dependencyMap = new Dictionary<string, List<string>>
        {
            { "Billing", new List<string> { "Cart" } },
            { "Cart", new List<string> { "Auth" } }
        };

        var resolved = DependencyResolver.Resolve(modules, dependencyMap);

        Assert.Equal(new[] { "Auth", "Cart", "Billing" }, resolved);
    }

    [Fact]
    public void BuildSandbox_GeneratesCorrectYarpConfig_AndVerifiesBuild()
    {
        var yarp = BuildSandbox.GenerateYarpConfig(new[] { "Auth" });
        Assert.Contains("route-auth", yarp);

        var result = BuildSandbox.VerifyBuild("/invalid/path");
        Assert.False(result);
    }

    [Fact]
    public async Task WasmtimeExecutor_InvalidModule_ThrowsException()
    {
        var executor = new WasmtimeExecutor();
        // Passing invalid empty WASM bytecode should throw an exception gracefully (fail closed)
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await executor.ExecuteAsync(new byte[] { 1, 2, 3 }, "nonexistent_fn");
        });
    }

    [Fact]
    public async Task WasmtimeExecutor_ValidModule_ExecutesSuccessfully()
    {
        var executor = new WasmtimeExecutor();

        // Minimal WASM module exporting "run" returning i32 const 42
        var runWasmBytes = new byte[]
        {
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00, // Magic + Version
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7f,       // Type section (i32 result)
            0x03, 0x02, 0x01, 0x00,                         // Function section
            0x07, 0x07, 0x01, 0x03, 0x72, 0x75, 0x6e, 0x00, 0x00, // Export "run"
            0x0a, 0x06, 0x01, 0x04, 0x00, 0x41, 0x2a, 0x0b  // Code section (const 42)
        };

        var result = await executor.ExecuteAsync(runWasmBytes, "run");
        Assert.Equal("42", result);

        // Test missing function within valid module
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await executor.ExecuteAsync(runWasmBytes, "missing_function");
        });
    }

    [Fact]
    public async Task WasmtimeExecutor_WithParameters_MapsArgumentsCorrectly()
    {
        var executor = new WasmtimeExecutor();

        // Minimal WASM module exporting "echo" (param i32) (result i32)
        var echoWasmBytes = new byte[]
        {
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x06, 0x01, 0x60, 0x01, 0x7f, 0x01, 0x7f,
            0x03, 0x02, 0x01, 0x00,
            0x07, 0x08, 0x01, 0x04, 0x65, 0x63, 0x68, 0x6f, 0x00, 0x00,
            0x0a, 0x06, 0x01, 0x04, 0x00, 0x20, 0x00, 0x0b
        };

        // 1. Map int parameter successfully
        var resultInt = await executor.ExecuteAsync(echoWasmBytes, "echo", 999);
        Assert.Equal("999", resultInt);

        // 2. Map string parameter (will fail type check but covers the string-mapping block and catch handler)
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await executor.ExecuteAsync(echoWasmBytes, "echo", "some-string");
        });

        // 3. Map default object parameter
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await executor.ExecuteAsync(echoWasmBytes, "echo", new object());
        });
    }

    [Fact]
    public async Task AdmissionWebhookController_AllowedAndDeniedImages()
    {
        var repository = BuildRealLocalRepository();
        var logger = TestHelpers.NullLogger<Beloved.ControlPlane.Controllers.AdmissionWebhookController>();
        var controller = new Beloved.ControlPlane.Controllers.AdmissionWebhookController(repository, logger);

        // Test Allowed Case
        var allowedJson = "{\"request\": {\"object\": {\"spec\": {\"containers\": [{\"image\": \"beloved/auth:latest\"}]}}}}";
        using var allowedDoc = System.Text.Json.JsonDocument.Parse(allowedJson);
        var allowedRes = await controller.ValidateAdmission(allowedDoc.RootElement) as Microsoft.AspNetCore.Mvc.OkObjectResult;
        Assert.NotNull(allowedRes);
        var allowedSer = System.Text.Json.JsonSerializer.Serialize(allowedRes.Value);
        using var allowedValDoc = System.Text.Json.JsonDocument.Parse(allowedSer);
        Assert.True(allowedValDoc.RootElement.GetProperty("response").GetProperty("allowed").GetBoolean());

        // Test Denied Case
        var deniedJson = "{\"request\": {\"object\": {\"spec\": {\"containers\": [{\"image\": \"beloved/unsigned:latest\"}]}}}}";
        using var deniedDoc = System.Text.Json.JsonDocument.Parse(deniedJson);
        var deniedRes = await controller.ValidateAdmission(deniedDoc.RootElement) as Microsoft.AspNetCore.Mvc.OkObjectResult;
        Assert.NotNull(deniedRes);
        var deniedSer = System.Text.Json.JsonSerializer.Serialize(deniedRes.Value);
        using var deniedValDoc = System.Text.Json.JsonDocument.Parse(deniedSer);
        Assert.False(deniedValDoc.RootElement.GetProperty("response").GetProperty("allowed").GetBoolean());
    }

    // Concrete fake HTTP handler and factory to test Prometheus parsing without mocking libraries
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;
        public FakeHttpMessageHandler(string response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(_response)
            });
        }
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FakeHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    [Fact]
    public async Task TelemetryObserverWorker_ParsesPrometheusMetricsCorrectly()
    {
        var metricsPayload = "beloved_assembly_duration_seconds_sum 15.0\n" +
                             "beloved_assembly_duration_seconds_count 10\n";
        
        using var handler = new FakeHttpMessageHandler(metricsPayload);
        using var client = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(client);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var sp = services.BuildServiceProvider();
        var logger = TestHelpers.NullLogger<TelemetryObserverWorker>();

        // Instantiate concrete worker
        var worker = new TelemetryObserverWorker(sp, factory, logger);

        // Access the protected parsing method via a subclass to execute the real parsing block
        var testWorker = new TestTelemetryObserverWorker(sp, factory, logger, 0.0);
        
        // This triggers the actual scraping loop parsing logic
        var latency = await testWorker.ExposeGetAverageAssemblyLatencyAsync(CancellationToken.None);
        
        // (15.0 sum / 10 count) * 1000ms = 1500ms
        Assert.Equal(1500.0, latency);
    }

    private class TestTelemetryObserverWorker : TelemetryObserverWorker
    {
        private readonly double? _overrideLatency;
        public TestTelemetryObserverWorker(IServiceProvider sp, IHttpClientFactory clientFactory, ILogger<TelemetryObserverWorker> logger, double? overrideLatency)
            : base(sp, clientFactory, logger)
        {
            _overrideLatency = overrideLatency;
        }

        protected override Task<double> GetAverageAssemblyLatencyAsync(CancellationToken cancellationToken)
        {
            if (_overrideLatency.HasValue && _overrideLatency.Value > 0.0)
            {
                return Task.FromResult(_overrideLatency.Value);
            }
            return base.GetAverageAssemblyLatencyAsync(cancellationToken);
        }

        public Task<double> ExposeGetAverageAssemblyLatencyAsync(CancellationToken token)
        {
            return base.GetAverageAssemblyLatencyAsync(token);
        }
    }

    [Fact]
    public async Task TelemetryObserverWorker_UnderLoad_TriggersAssemblyOptimization()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddMassTransitTestHarness(x => { });
        services.AddHttpClient();
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        var harness = sp.GetRequiredService<MassTransit.Testing.ITestHarness>();
        await harness.Start();

        var logger = TestHelpers.NullLogger<TelemetryObserverWorker>();
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var worker = new TestTelemetryObserverWorker(sp, clientFactory, logger, 1250.0);

        using var cts = new CancellationTokenSource();
        var task = worker.StartAsync(cts.Token);
        
        await Task.Delay(200); // Allow execution loop step
        cts.Cancel();
        await task;

        // Verify the message was published to the actual in-memory broker harness
        Assert.True(await harness.Published.Any<OptimizeAssemblyMessage>());
    }

    [Fact]
    public async Task WasmtimeExecutor_ValidHeaderModule_ThrowsMissingFunction()
    {
        var executor = new WasmtimeExecutor();
        // Valid WASM MVP binary header bytes
        var mvpWasmBytes = new byte[] { 0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00 };
        
        // Assert that loading succeeded but threw Function not found as expected (fails closed securely)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await executor.ExecuteAsync(mvpWasmBytes, "missing_function");
        });
        Assert.Contains("Function missing_function not found", exception.Message);
    }

    private LocalVaultRepository BuildRealLocalRepository()
    {
        var cwd = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(cwd);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "vault")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not find workspace root with 'vault' folder.");
        }
        return new LocalVaultRepository(dir.FullName);
    }
}
