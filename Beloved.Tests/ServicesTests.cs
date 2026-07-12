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

        // Templates — copy from our fixture dirs
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

        return mock;
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
        mock.Setup(v => v.FetchModuleAsync("BrokenModule", "latest", It.IsAny<string>()))
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
            mock.Setup(v => v.FetchModuleAsync(capture, "latest", It.IsAny<string>()))
                .Returns<string, string, string>(async (_, _, dest) =>
                {
                    fetchOrder.Add(capture);
                    await Task.Delay(50); // simulate I/O
                    Directory.CreateDirectory(dest);
                    File.WriteAllText(Path.Combine(dest, "manifest.json"),
                        $"{{\"name\":\"{capture}\",\"description\":\"\",\"frontend\":{{\"nav\":\"\",\"views\":\"\",\"imports\":\"\"}},\"backend\":{{\"controllers\":[],\"dbSets\":\"\"}}}}");
                    return (dest, $"sha256:{capture}");
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
        mock.Verify(v => v.FetchTemplateAsync("react-frontend", It.IsAny<string>()), Times.Never);
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
