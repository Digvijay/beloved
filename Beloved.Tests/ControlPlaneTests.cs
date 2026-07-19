using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Beloved.ControlPlane.Services;
using Beloved.AssemblyEngine;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Beloved.Tests
{
    public class ControlPlaneWebApplicationFactory : WebApplicationFactory<Beloved.ControlPlane.Controllers.ControlPlaneController>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"beloved_test_{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:BelovedDb", DbPath);

            builder.ConfigureTestServices(services =>
            {
                // 1. Mock external communications (OciVaultRepository HttpClient)
                services.AddTransient<IVaultRepository>(sp =>
                {
                    var mock = new Mock<IVaultRepository>();
                    mock.Setup(r => r.FetchTemplateInMemoryAsync(It.IsAny<string>()))
                        .ReturnsAsync((new Dictionary<string, byte[]>(), "sha-mock"));
                    return mock.Object;
                });

                // 2. Prevent background workers from trying to run or calling external APIs during testing
                var emailProcessor = services.SingleOrDefault(d => d.ImplementationType == typeof(EmailBackgroundProcessor));
                if (emailProcessor != null) services.Remove(emailProcessor);

                // 3. Override MassTransit to use InMemory transport to avoid needing local RabbitMQ running
                services.AddMassTransitTestHarness(cfg =>
                {
                    // standard in-memory test harness
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                try
                {
                    if (File.Exists(DbPath)) File.Delete(DbPath);
                }
                catch { }
            }
        }
    }

    public class ControlPlaneTests : IClassFixture<ControlPlaneWebApplicationFactory>
    {
        private readonly ControlPlaneWebApplicationFactory _factory;

        public ControlPlaneTests(ControlPlaneWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private async Task SeedDefaultTenantAsync(BelovedDbContext db)
        {
            if (!await db.Tenants.AnyAsync(t => t.ApiKey == "test-api-key"))
            {
                var tenant = new Tenant
                {
                    Name = "IntegrationTestTenant",
                    ApiKey = "test-api-key"
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task HealthEndpoints_ReturnHealthyAndReady()
        {
            var client = _factory.CreateClient();

            var liveResponse = await client.GetAsync("/healthz/live");
            Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
            var liveContent = await liveResponse.Content.ReadAsStringAsync();
            Assert.Contains("Healthy", liveContent);

            var readyResponse = await client.GetAsync("/healthz/ready");
            Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
            var readyContent = await readyResponse.Content.ReadAsStringAsync();
            Assert.Contains("Ready", readyContent);
        }

        [Fact]
        public async Task UnauthenticatedApiCall_ToProjects_ReturnsUnauthorized()
        {
            var client = _factory.CreateClient();

            // Web API requires ApiKey authentication header by default
            var response = await client.GetAsync("/api/projects");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticatedApiCall_GetProjects_ReturnsEmptyList()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();
            await SeedDefaultTenantAsync(db);

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-KEY", "test-api-key");

            var response = await client.GetAsync("/api/projects");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var projects = await response.Content.ReadFromJsonAsync<List<Project>>();
            Assert.NotNull(projects);
            Assert.Empty(projects);
        }

        [Fact]
        public async Task AuthenticatedApiCall_CreateProject_Succeeds()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();
            await SeedDefaultTenantAsync(db);

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-KEY", "test-api-key");

            var newProject = new
            {
                name = "Integration Test App",
                description = "My test description"
            };

            var createResponse = await client.PostAsJsonAsync("/api/projects", newProject);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<Project>();
            Assert.NotNull(created);
            Assert.Equal("Integration Test App", created.Name);

            // Fetch to ensure it was created and associated correctly
            var listResponse = await client.GetAsync("/api/projects");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var projects = await listResponse.Content.ReadFromJsonAsync<List<Project>>();
            Assert.Single(projects);
            Assert.Equal("Integration Test App", projects[0].Name);
        }

        [Fact]
        public async Task BillingEndpoint_ReturnsMockStatus()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();
            await SeedDefaultTenantAsync(db);

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-KEY", "test-api-key");

            var response = await client.GetAsync("/api/billing/plan");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var billingInfo = await response.Content.ReadAsStringAsync();
            Assert.Contains("Free", billingInfo);
        }
    }
}
