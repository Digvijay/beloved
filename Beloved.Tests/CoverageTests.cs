using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Beloved.AssemblyEngine;
using Beloved.ControlPlane.Controllers;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Middleware;
using Beloved.ControlPlane.Models;
using Beloved.ControlPlane.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Stripe;

namespace Beloved.Tests
{
    public class CoverageTests
    {
        // ── 1. Fakes for HTTP & DI Setup ─────────────────────────────────────

        private class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFunc;

            public FakeHttpMessageHandler(string jsonResponse, HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                _responseFunc = _ => new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
                };
            }

            public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
            {
                _responseFunc = responseFunc;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_responseFunc(request));
            }
        }

        private class FakeHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;
            public FakeHttpClientFactory(HttpClient client) => _client = client;
            public HttpClient CreateClient(string name) => _client;
        }

        private static BelovedDbContext CreateInMemoryDb()
        {
            var options = new DbContextOptionsBuilder<BelovedDbContext>()
                .UseInMemoryDatabase(databaseName: $"BelovedDb_{Guid.NewGuid():N}")
                .Options;
            var db = new BelovedDbContext(options);
            db.Database.EnsureCreated();
            return db;
        }

        // ── 2. LLM Providers Tests ──────────────────────────────────────────

        [Fact]
        public async Task OllamaLlmProvider_MapIntentAndStitch_Succeeds()
        {
            var jsonResp = "{\"choices\":[{\"message\":{\"content\":\"{\\\"appName\\\":\\\"OllamaApp\\\",\\\"modules\\\":[\\\"Auth\\\"],\\\"database\\\":\\\"SQLite\\\",\\\"authStrategy\\\":\\\"None\\\",\\\"target\\\":\\\"WebAndApi\\\"}\"}}]}";
            using var handler = new FakeHttpMessageHandler(jsonResp);
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };

            var provider = new OllamaLlmProvider(client, "test-model");
            var blueprint = await provider.MapIntentAsync("create auth app", new[] { "Auth" });

            Assert.NotNull(blueprint);
            Assert.Equal("OllamaApp", blueprint.AppName);

            var stitchResp = "{\"choices\":[{\"message\":{\"content\":\"```csharp\\nmodified-code\\n```\"}}]}";
            using var handlerStitch = new FakeHttpMessageHandler(stitchResp);
            using var clientStitch = new HttpClient(handlerStitch) { BaseAddress = new Uri("http://localhost:11434") };
            var providerStitch = new OllamaLlmProvider(clientStitch, "test-model");
            var result = await providerStitch.StitchFileAsync("original-code", "add variable");
            Assert.Equal("modified-code", result);
        }

        [Fact]
        public async Task OpenAiLlmProvider_MapIntentAndStitch_Succeeds()
        {
            var jsonResp = "{\"choices\":[{\"message\":{\"content\":\"{\\\"appName\\\":\\\"OpenAiApp\\\",\\\"modules\\\":[\\\"Auth\\\"],\\\"database\\\":\\\"SQLite\\\",\\\"authStrategy\\\":\\\"None\\\",\\\"target\\\":\\\"WebAndApi\\\"}\"}}]}";
            using var handler = new FakeHttpMessageHandler(jsonResp);
            using var client = new HttpClient(handler);

            var provider = new OpenAiLlmProvider(client, "key", "gpt-model");
            var blueprint = await provider.MapIntentAsync("create auth app", new[] { "Auth" });

            Assert.NotNull(blueprint);
            Assert.Equal("OpenAiApp", blueprint.AppName);

            var stitchResp = "{\"choices\":[{\"message\":{\"content\":\"modified-code\"}}]}";
            using var handlerStitch = new FakeHttpMessageHandler(stitchResp);
            using var clientStitch = new HttpClient(handlerStitch);
            var providerStitch = new OpenAiLlmProvider(clientStitch, "key", "gpt-model");
            var result = await providerStitch.StitchFileAsync("original-code", "add variable");
            Assert.Equal("modified-code", result);
        }

        [Fact]
        public async Task GeminiLlmProvider_MapIntentAndStitch_Succeeds()
        {
            var jsonResp = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"appName\\\":\\\"GeminiApp\\\",\\\"modules\\\":[\\\"Auth\\\"],\\\"database\\\":\\\"SQLite\\\",\\\"authStrategy\\\":\\\"None\\\",\\\"target\\\":\\\"WebAndApi\\\"}\"}]}}]}";
            using var handler = new FakeHttpMessageHandler(jsonResp);
            using var client = new HttpClient(handler);

            var provider = new GeminiLlmProvider(client, "key", "gemini-model");
            var blueprint = await provider.MapIntentAsync("create auth app", new[] { "Auth" });

            Assert.NotNull(blueprint);
            Assert.Equal("GeminiApp", blueprint.AppName);

            var stitchResp = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"modified-code\"}]}}]}";
            using var handlerStitch = new FakeHttpMessageHandler(stitchResp);
            using var clientStitch = new HttpClient(handlerStitch);
            var providerStitch = new GeminiLlmProvider(clientStitch, "key", "gemini-model");
            var result = await providerStitch.StitchFileAsync("original-code", "add variable");
            Assert.Equal("modified-code", result);
        }

        [Fact]
        public async Task ClaudeLlmProvider_MapIntentAndStitch_Succeeds()
        {
            var jsonResp = "{\"content\":[{\"text\":\"{\\\"appName\\\":\\\"ClaudeApp\\\",\\\"modules\\\":[\\\"Auth\\\"],\\\"database\\\":\\\"SQLite\\\",\\\"authStrategy\\\":\\\"None\\\",\\\"target\\\":\\\"WebAndApi\\\"}\"}]}";
            using var handler = new FakeHttpMessageHandler(jsonResp);
            using var client = new HttpClient(handler);

            var provider = new ClaudeLlmProvider(client, "key", "claude-model");
            var blueprint = await provider.MapIntentAsync("create auth app", new[] { "Auth" });

            Assert.NotNull(blueprint);
            Assert.Equal("ClaudeApp", blueprint.AppName);

            var stitchResp = "{\"content\":[{\"text\":\"modified-code\"}]}";
            using var handlerStitch = new FakeHttpMessageHandler(stitchResp);
            using var clientStitch = new HttpClient(handlerStitch);
            var providerStitch = new ClaudeLlmProvider(clientStitch, "key", "claude-model");
            var result = await providerStitch.StitchFileAsync("original-code", "add variable");
            Assert.Equal("modified-code", result);
        }

        // ── 3. Quota Middleware Tests ────────────────────────────────────────

        [Fact]
        public async Task QuotaMiddleware_GatesAssembleRequests_WhenQuotaExceeded()
        {
            var db = CreateInMemoryDb();
            var tenant = new Tenant
            {
                Name = "GatedTenant",
                ApiKey = "exhausted-key",
                Plan = TenantPlan.Free
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var quotaServiceMock = new Mock<IQuotaService>();
            quotaServiceMock.Setup(q => q.HasQuotaAsync(tenant.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            quotaServiceMock.Setup(q => q.GetUsedThisMonthAsync(tenant.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(5);

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton(quotaServiceMock.Object);
            var sp = services.BuildServiceProvider();

            var context = new DefaultHttpContext();
            context.Request.Path = "/api/assemble";
            context.Request.Method = "POST";
            context.Request.Headers["X-Api-Key"] = "exhausted-key";
            context.RequestServices = sp;

            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new QuotaMiddleware(next);
            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(429, context.Response.StatusCode);
        }

        // ── 4. Email Outbox Processor Tests ──────────────────────────────────

        [Fact]
        public async Task EmailBackgroundProcessor_SendsPendingEmails_AndHandlesRetries()
        {
            var db = CreateInMemoryDb();
            var pendingJob = new EmailQueueJob
            {
                RecipientEmail = "test@example.com",
                Subject = "Hello",
                Body = "Body",
                Status = "Pending"
            };
            var failedJob = new EmailQueueJob
            {
                RecipientEmail = "fail@example.com",
                Subject = "Hello Fail",
                Body = "Body Fail",
                Status = "Failed",
                RetryCount = 1
            };
            db.EmailQueueJobs.AddRange(pendingJob, failedJob);
            await db.SaveChangesAsync();

            var mailClientMock = new Mock<IMailClient>();
            mailClientMock.Setup(m => m.SendEmailAsync("test@example.com", "Hello", "Body"))
                .Returns(Task.CompletedTask);
            mailClientMock.Setup(m => m.SendEmailAsync("fail@example.com", "Hello Fail", "Body Fail"))
                .ThrowsAsync(new Exception("SMTP down"));

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton(mailClientMock.Object);
            var sp = services.BuildServiceProvider();

            var logger = TestHelpers.NullLogger<EmailBackgroundProcessor>();
            var processor = new EmailBackgroundProcessor(sp, logger);

            using var cts = new CancellationTokenSource();
            var task = processor.StartAsync(cts.Token);
            await Task.Delay(100);
            cts.Cancel();
            await task;

            // Fetch jobs from DB to verify state changes
            var updatedPending = await db.EmailQueueJobs.FindAsync(pendingJob.Id);
            Assert.NotNull(updatedPending);
            Assert.Equal("Sent", updatedPending.Status);

            var updatedFailed = await db.EmailQueueJobs.FindAsync(failedJob.Id);
            Assert.NotNull(updatedFailed);
            Assert.Equal("Failed", updatedFailed.Status);
            Assert.Equal(2, updatedFailed.RetryCount);
        }

        // ── 5. Cached Vault Repository Tests ─────────────────────────────────

        [Fact]
        public async Task CachedVaultRepository_CachesInMemoryPulls()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var innerMock = new Mock<IVaultRepository>();
            innerMock.Setup(i => i.FetchModuleInMemoryAsync("Auth", "latest"))
                .ReturnsAsync((new Dictionary<string, byte[]> { ["file.txt"] = new byte[] { 1 } }, "digest-1"));

            var cachedRepo = new CachedVaultRepository(innerMock.Object, cache);

            var res1 = await cachedRepo.FetchModuleInMemoryAsync("Auth", "latest");
            var res2 = await cachedRepo.FetchModuleInMemoryAsync("Auth", "latest");

            Assert.Equal("digest-1", res1.digest);
            Assert.Equal("digest-1", res2.digest);

            // Verify inner was called exactly once (cached on second request)
            innerMock.Verify(i => i.FetchModuleInMemoryAsync("Auth", "latest"), Times.Once);
        }

        // ── 6. OAuth Service Tests ──────────────────────────────────────────

        [Fact]
        public async Task OAuthService_ExchangesGitHubCodeSuccessfully()
        {
            var db = CreateInMemoryDb();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OAuth:GitHub:ClientId"] = "git-id",
                    ["OAuth:GitHub:ClientSecret"] = "git-secret",
                    ["OAuth:GitHub:RedirectUri"] = "http://redirect"
                })
                .Build();

            // Mock OAuth JSON endpoints: token, user profile, emails list
            var handler = new FakeHttpMessageHandler(req =>
            {
                var url = req.RequestUri?.AbsoluteUri ?? "";
                if (url.Contains("access_token"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"mock-token\"}") };
                if (url.Contains("/user/emails"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"email\":\"git@example.com\",\"primary\":true,\"verified\":true}]") };
                if (url.Contains("/user"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":12345,\"login\":\"gituser\",\"name\":\"Git User\",\"avatar_url\":\"http://avatar\"}") };
                
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var httpClient = new HttpClient(handler);
            var factory = new FakeHttpClientFactory(httpClient);

            var oauthService = new OAuthService(factory, db, config);
            var user = await oauthService.ExchangeGitHubCodeAsync("github-code");

            Assert.NotNull(user);
            Assert.Equal("git@example.com", user.Email);
            Assert.Equal("Git User", user.DisplayName);
            Assert.Equal("github", user.Provider);
        }

        // ── 7. Stripe Checkout and Webhooks Tests ─────────────────────────────

        private class TestBillingController : BillingController
        {
            private readonly Event _eventToReturn;
            public TestBillingController(BelovedDbContext db, IConfiguration config, IEmailSender emailSender, Event eventToReturn)
                : base(db, config, emailSender)
            {
                _eventToReturn = eventToReturn;
            }

            protected override Event ParseStripeEvent(string payload, string sigHeader) => _eventToReturn;
        }

        [Fact]
        public async Task BillingController_UpgradeAndDowngradeWebhooks_ProcessSuccessfully()
        {
            var db = CreateInMemoryDb();
            var tenant = new Tenant
            {
                Name = "BillingTenant",
                ApiKey = "stripe-key",
                Plan = TenantPlan.Free
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var config = new ConfigurationBuilder().Build();
            var emailMock = new Mock<IEmailSender>();

            var mockEvent = new Event
            {
                Type = EventTypes.CheckoutSessionCompleted,
                Data = new EventData
                {
                    Object = new Stripe.Checkout.Session
                    {
                        CustomerId = "cus_test",
                        SubscriptionId = "sub_test",
                        Metadata = new Dictionary<string, string> { ["beloved_tenant_id"] = tenant.Id.ToString() }
                    }
                }
            };

            var controller = new TestBillingController(db, config, emailMock.Object, mockEvent);

            // 1. Resolve tenant plan info
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.Request.Headers["X-Api-Key"] = "stripe-key";

            var planResult = await controller.GetPlan() as OkObjectResult;
            Assert.NotNull(planResult);

            controller.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
            controller.Request.Headers["Stripe-Signature"] = "sig-placeholder";

            var webhookResult = await controller.StripeWebhook() as OkResult;
            Assert.NotNull(webhookResult);

            // Reload and check plan upgrade
            var upgradedTenant = await db.Tenants.FindAsync(tenant.Id);
            Assert.NotNull(upgradedTenant);
            Assert.Equal(TenantPlan.Pro, upgradedTenant.Plan);
        }

        // ── 8. Build Sandbox Success Paths ────────────────────────────────────

        [Fact]
        public async Task BuildSandbox_ValidConsoleApp_ReturnsTrue()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "SandboxTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var csprojContent = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net9.0</TargetFramework>\n  </PropertyGroup>\n</Project>";
                await System.IO.File.WriteAllTextAsync(Path.Combine(tempDir, "TestApp.csproj"), csprojContent);
                await System.IO.File.WriteAllTextAsync(Path.Combine(tempDir, "Program.cs"), "System.Console.WriteLine(\"Hello\");");

                var success = BuildSandbox.VerifyBuild(tempDir);
                Assert.True(success);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // ── 9. Auth Controller Lifecycle Tests ────────────────────────────────

        [Fact]
        public async Task AuthController_RedirectsAndRefreshesTokens()
        {
            var db = CreateInMemoryDb();
            var oauthMock = new Mock<IOAuthService>();
            oauthMock.Setup(o => o.BuildGitHubAuthUrl(It.IsAny<string>()))
                .Returns("http://github-auth-url");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = "super-secret-key-123-long-enough-to-be-secure-for-tests"
                })
                .Build();
            var jwtService = new JwtTokenService(config);

            var controller = new AuthController(oauthMock.Object, jwtService, db);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            // Redirects checks
            var loginRes = controller.LoginGitHub() as RedirectResult;
            Assert.NotNull(loginRes);
            Assert.Equal("http://github-auth-url", loginRes.Url);

            // Refresh token simulation
            var user = new BelovedUser { Email = "user@example.com", Provider = "github", ProviderSubject = "123" };
            db.Users.Add(user);
            var refToken = new RefreshToken
            {
                UserId = user.Id,
                Token = "ref-123",
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            };
            db.RefreshTokens.Add(refToken);
            await db.SaveChangesAsync();

            var refreshResult = await controller.RefreshToken(new RefreshTokenRequest("ref-123"), CancellationToken.None) as OkObjectResult;
            Assert.NotNull(refreshResult);
        }

        // ── 10. Orgs Controller Lifecycle Tests ───────────────────────────────

        [Fact]
        public async Task OrgsController_FullCycle_CreatesAndInvites()
        {
            var db = CreateInMemoryDb();
            var userId = Guid.NewGuid();
            var user = new BelovedUser { Id = userId, Email = "owner@example.com", DisplayName = "Owner User", Provider = "github", ProviderSubject = "123" };
            var invitee = new BelovedUser { Email = "invitee@example.com", DisplayName = "Invitee User", Provider = "google", ProviderSubject = "456" };
            db.Users.AddRange(user, invitee);
            await db.SaveChangesAsync();

            var controller = new OrgsController(db);
            var claims = new[] { new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, userId.ToString()) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };

            // 1. Create Org
            var createRes = await controller.CreateOrg(new OrgsController.CreateOrgRequest("My Team", "my-team"), CancellationToken.None) as CreatedAtActionResult;
            Assert.NotNull(createRes);

            // 2. List Orgs
            var listRes = await controller.ListOrgs(CancellationToken.None) as OkObjectResult;
            Assert.NotNull(listRes);

            // 3. Get Details
            var getRes = await controller.GetOrg("my-team", CancellationToken.None) as OkObjectResult;
            Assert.NotNull(getRes);

            // 4. Invite Member
            var inviteRes = await controller.InviteMember("my-team", new OrgsController.InviteMemberRequest("invitee@example.com", OrgRole.Admin), CancellationToken.None) as OkObjectResult;
            Assert.NotNull(inviteRes);

            // 5. Quota Rollup Check
            var usageRes = await controller.GetOrgUsage("my-team", CancellationToken.None) as OkObjectResult;
            Assert.NotNull(usageRes);
        }

        // ── 11. Webhook Dispatcher Tests ─────────────────────────────────────

        [Fact]
        public async Task WebhookDispatcher_DispatchesEvents_Successfully()
        {
            var db = CreateInMemoryDb();
            var tenantId = Guid.NewGuid();
            var webhook = new Beloved.ControlPlane.Models.Webhook
            {
                TenantId = tenantId,
                Url = "http://my-webhook.target",
                Secret = "webhook-secret",
                Events = "assembly.completed",
                IsActive = true
            };
            db.Webhooks.Add(webhook);
            await db.SaveChangesAsync();

            var handler = new FakeHttpMessageHandler("{}", HttpStatusCode.OK);
            var client = new HttpClient(handler);
            var factory = new FakeHttpClientFactory(client);

            var services = new ServiceCollection();
            services.AddSingleton(db);
            var sp = services.BuildServiceProvider();

            var logger = TestHelpers.NullLogger<WebhookDispatcher>();
            var dispatcher = new WebhookDispatcher(factory, sp, logger);

            await dispatcher.DispatchAsync(tenantId, "assembly.completed", new { jobId = "job-1" }, CancellationToken.None);
        }

        // ── 12. Sandbox Orchestrator Tests ───────────────────────────────────

        [Fact]
        public async Task SandboxOrchestrator_ManagesSandboxes_Successfully()
        {
            var outputStoreMock = new Mock<IOutputStore>();
            // Return null to cover "Artifact not found" error path
            outputStoreMock.Setup(o => o.GetArtifactAsync("invalid-job"))
                .ReturnsAsync((Stream?)null);

            var orchestrator = new SandboxOrchestrator(outputStoreMock.Object);
            var (success, error, url) = await orchestrator.StartSandboxAsync("invalid-job");

            Assert.False(success);
            Assert.Contains("Artifact not found", error);

            // Verify clean StopSandbox execution
            await orchestrator.StopSandboxAsync();
        }

        // ── 13. Assembly Job Consumer Tests ──────────────────────────────────

        [Fact]
        public async Task AssemblyJobConsumer_ConsumesJob_Successfully()
        {
            var db = CreateInMemoryDb();
            var job = new Beloved.ControlPlane.Models.AssemblyJob
            {
                QueueJobId = "job-id",
                Status = "Queued",
                BlueprintJson = "{}"
            };
            db.AssemblyJobs.Add(job);
            await db.SaveChangesAsync();

            var vaultMock = new Mock<IVaultRepository>();
            var compilerMock = new Mock<AssemblyCompiler>(vaultMock.Object, new List<IAssemblyPlugin>(), null);
            compilerMock.Setup(c => c.AssembleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOutputStore>(), It.IsAny<Action<string>>()))
                .ReturnsAsync(new AssemblyResult { Success = true, SbomJson = "{}" });

            var outputStoreMock = new Mock<IOutputStore>();
            var hubContextMock = new Mock<Microsoft.AspNetCore.SignalR.IHubContext<Beloved.ControlPlane.Hubs.AssemblyHub>>();
            var clientsMock = new Mock<Microsoft.AspNetCore.SignalR.IHubClients>();
            var clientProxyMock = new Mock<Microsoft.AspNetCore.SignalR.IClientProxy>();
            clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
            hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

            var logger = TestHelpers.NullLogger<AssemblyJobConsumer>();

            var consumer = new AssemblyJobConsumer(compilerMock.Object, outputStoreMock.Object, hubContextMock.Object, db, logger);

            var mockContext = new Mock<MassTransit.ConsumeContext<AssemblyJobMessage>>();
            mockContext.Setup(c => c.Message).Returns(new AssemblyJobMessage("job-id", new Blueprint { AppName = "ConsumerApp" }));

            await consumer.Consume(mockContext.Object);

            var updatedJob = await db.AssemblyJobs.FirstOrDefaultAsync(j => j.QueueJobId == "job-id");
            Assert.NotNull(updatedJob);
            Assert.Equal("Completed", updatedJob.Status);
        }

        // ── 14. Control Plane Controller Tests ────────────────────────────────

        [Fact]
        public async Task ControlPlaneController_ExecutesEndpoints_Successfully()
        {
            var db = CreateInMemoryDb();
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();

            var project = new Beloved.ControlPlane.Models.Project
            {
                Id = projectId,
                TenantId = tenantId,
                Name = "Project Alpha"
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            var envMock = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            var vaultMock = new Mock<IVaultRepository>();
            vaultMock.Setup(v => v.ListModulesAsync())
                .ReturnsAsync(new List<string> { "Auth" });

            var compilerMock = new Mock<AssemblyCompiler>(vaultMock.Object, new List<IAssemblyPlugin>(), null);
            var llmMock = new Mock<ILlmProvider>();
            llmMock.Setup(l => l.MapIntentAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Blueprint { AppName = "IntentApp" });
            llmMock.Setup(l => l.RefineBlueprintAsync(It.IsAny<Blueprint>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Blueprint { AppName = "RefinedApp" });

            var publishMock = new Mock<MassTransit.IPublishEndpoint>();
            var outputStoreMock = new Mock<IOutputStore>();
            var sandboxMock = new Mock<SandboxOrchestrator>(outputStoreMock.Object);
            sandboxMock.Setup(s => s.StartSandboxAsync(It.IsAny<string>()))
                .ReturnsAsync((true, "", "http://sandbox-url"));

            var controller = new ControlPlaneController(envMock.Object, vaultMock.Object, compilerMock.Object, llmMock.Object, publishMock.Object, outputStoreMock.Object, sandboxMock.Object, db);

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, tenantId.ToString()) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };

            // 1. GetModules
            var modulesRes = await controller.GetModules() as OkObjectResult;
            Assert.NotNull(modulesRes);

            // 2. MapIntent
            var intentRes = await controller.MapIntent(new ControlPlaneController.IntentRequest { Prompt = "need auth" }) as OkObjectResult;
            Assert.NotNull(intentRes);

            // 3. RefineBlueprint
            var refineRes = await controller.RefineBlueprint(new ControlPlaneController.RefineBlueprintRequest
            {
                CurrentBlueprint = new Blueprint { AppName = "App" },
                RefinePrompt = "make it better"
            }) as OkObjectResult;
            Assert.NotNull(refineRes);

            // 4. Assemble
            var jsonBlueprint = JsonSerializer.SerializeToElement(new Blueprint { AppName = "AssembleApp" }, AssemblyJsonContext.Default.Blueprint);
            var assembleRes = await controller.Assemble(new ControlPlaneController.AssembleRequest
            {
                ProjectId = projectId.ToString(),
                Blueprint = jsonBlueprint
            }, db) as AcceptedResult;
            Assert.NotNull(assembleRes);

            // 5. Webhook creation/list/delete lifecycle
            var regRes = await controller.RegisterWebhook(new ControlPlaneController.RegisterWebhookRequest
            {
                Url = "http://my-hook",
                Events = "job.completed",
                Secret = "xyz"
            }) as OkObjectResult;
            Assert.NotNull(regRes);

            var listRes = await controller.ListWebhooks() as OkObjectResult;
            Assert.NotNull(listRes);

            var webhooksList = listRes.Value as System.Collections.IEnumerable;
            Assert.NotNull(webhooksList);
            var enumWebhook = webhooksList.GetEnumerator();
            enumWebhook.MoveNext();
            var createdWebhook = enumWebhook.Current;
            Assert.NotNull(createdWebhook);

            // Extract ID via reflection since it is an anonymous type
            var idProperty = createdWebhook.GetType().GetProperty("Id");
            var webhookId = (Guid)idProperty!.GetValue(createdWebhook)!;

            var deleteRes = await controller.DeleteWebhook(webhookId) as NoContentResult;
            Assert.NotNull(deleteRes);

            // 6. Sandbox actions
            var startSandboxRes = await controller.StartPreview(new ControlPlaneController.PreviewRequest { JobId = "job-1" }) as OkObjectResult;
            Assert.NotNull(startSandboxRes);

            var stopSandboxRes = await controller.StopPreview() as OkObjectResult;
            Assert.NotNull(stopSandboxRes);
        }

        [Fact]
        public async Task AssemblyCompiler_WithMissingPlaceholdersAndLlm_TriggersAiStitching()
        {
            var vaultMock = new Mock<IVaultRepository>();
            // Return empty files dictionary but include App.tsx without placeholders
            var frontendFiles = new Dictionary<string, byte[]>
            {
                ["src/App.tsx"] = System.Text.Encoding.UTF8.GetBytes("const App = () => <div>No Placeholders</div>;"),
                ["src/index.css"] = System.Text.Encoding.UTF8.GetBytes(":root { --primary: #000; }")
            };
            var backendFiles = new Dictionary<string, byte[]>();

            vaultMock.Setup(v => v.FetchTemplateInMemoryAsync("react-frontend"))
                .ReturnsAsync((frontendFiles, "sha-react"));
            vaultMock.Setup(v => v.FetchTemplateInMemoryAsync("dotnet-backend"))
                .ReturnsAsync((backendFiles, "sha-dotnet"));
            vaultMock.Setup(v => v.FetchModuleInMemoryAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((new Dictionary<string, byte[]>
                {
                    ["manifest.json"] = System.Text.Encoding.UTF8.GetBytes("{\"Name\":\"Auth\",\"Frontend\":{\"Nav\":\"<Link>Auth</Link>\",\"Views\":\"<AuthView/>\"}}")
                }, "sha-auth"));

            var llmMock = new Mock<ILlmProvider>();
            llmMock.Setup(l => l.StitchFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("stitched-content");

            var compiler = new AssemblyCompiler(vaultMock.Object, new List<IAssemblyPlugin>(), llmMock.Object);

            var outputStoreMock = new Mock<IOutputStore>();
            var blueprint = new Blueprint
            {
                AppName = "StitchApp",
                Modules = new List<string> { "Auth" }
            };
            var result = await compiler.AssembleAsync(JsonSerializer.Serialize(blueprint), "job-1", outputStoreMock.Object);

            Assert.True(result.Success);
            llmMock.Verify(l => l.StitchFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }
    }
}
