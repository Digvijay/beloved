using MassTransit;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Auth;
using Beloved.ControlPlane.Services;
using Beloved.AssemblyEngine;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add Database Connection (supporting both SQLite and PostgreSQL)
var dbProvider = builder.Configuration["DatabaseProvider"] ?? "SQLite";
builder.Services.AddDbContext<BelovedDbContext>(options =>
{
    if (dbProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        var connStr = builder.Configuration.GetConnectionString("PostgresDb")
            ?? "Host=localhost;Database=beloved;Username=postgres;Password=postgres";
        options.UseNpgsql(connStr);
    }
    else
    {
        var dbPath = builder.Configuration.GetConnectionString("BelovedDb")
            ?? Path.Join(Directory.GetCurrentDirectory(), "beloved.db");
        options.UseSqlite($"Data Source={dbPath}");
    }
});

// Add Authentication — API Key scheme (existing) + JWT Bearer
var jwtSecret  = builder.Configuration["Jwt:Secret"]   ?? "beloved-dev-secret-change-in-production";
var jwtIssuer  = builder.Configuration["Jwt:Issuer"]   ?? "beloved.build";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "beloved.build";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = ApiKeyAuthenticationOptions.DefaultScheme;
    options.DefaultChallengeScheme    = ApiKeyAuthenticationOptions.DefaultScheme;
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationOptions.DefaultScheme, options => { })
.AddJwtBearer("JwtBearer", options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = jwtIssuer,
        ValidAudience            = jwtAudience,
        IssuerSigningKey         = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

// Identity services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IOAuthService, OAuthService>();
builder.Services.AddHttpClient(); // IHttpClientFactory for OAuthService

// OpenAPI Generation
builder.Services.AddOpenApi();



// Configure OpenTelemetry for tracing and Prometheus metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Beloved.ControlPlane", "1.0.0"))
    .WithTracing(tracing => tracing
        .AddSource("Beloved.AssemblyEngine")
        .AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Beloved.AssemblyEngine")
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddMemoryCache();

// Register the Vault Repository using the Typed HttpClient pattern (Fowler/Edwards standard)
builder.Services.AddTransient<ISignatureVerifier, PemSignatureVerifier>();

builder.Services.AddHttpClient<OciVaultRepository>(client => 
{
    client.BaseAddress = new Uri("http://localhost:5001");
    // 30s assembly fetches; prevent runaway long-pulls
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // Recycle connections every 5 minutes — prevents DNS staleness in Kubernetes
    PooledConnectionLifetime    = TimeSpan.FromMinutes(5),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    // Allow up to 50 concurrent connections to the OCI registry per worker pod
    MaxConnectionsPerServer     = 50,
    // Enable HTTP/2 multiplexing for OCI layer downloads
    EnableMultipleHttp2Connections = true,
});

// Decorate the OciVaultRepository registration
builder.Services.AddTransient<IVaultRepository>(sp =>
{
    var inner = sp.GetRequiredService<OciVaultRepository>();
    var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
    return new CachedVaultRepository(inner, cache);
});

// Add SignalR with Redis Backplane if configured
var signalRBuilder = builder.Services.AddSignalR();
var redisConn = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrEmpty(redisConn))
{
    signalRBuilder.AddStackExchangeRedis(redisConn);
}

// Register Queue and Worker
builder.Services.AddSingleton<IOutputStore, Beloved.ControlPlane.Services.LocalDiskOutputStore>();
builder.Services.AddSingleton<Beloved.ControlPlane.Services.SandboxOrchestrator>();
builder.Services.AddHostedService<Beloved.ControlPlane.Services.EmailBackgroundProcessor>();
builder.Services.AddHostedService<Beloved.ControlPlane.Services.TelemetryObserverWorker>();

// Register Wasmtime Executor
builder.Services.AddTransient<IWasmExecutor, WasmtimeExecutor>();

// MassTransit & RabbitMQ Setup
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AssemblyJobConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ReceiveEndpoint("assembly-jobs", e =>
        {
            e.ConfigureConsumer<AssemblyJobConsumer>(context);
        });
    });
});

// Register AssemblyCompiler
builder.Services.AddTransient<AssemblyCompiler>();

// Register Assembly Plugins
builder.Services.AddTransient<IAssemblyPlugin, AnalyticsInjectionPlugin>();

// Register Quota Service
builder.Services.AddScoped<IQuotaService, Beloved.ControlPlane.Services.QuotaService>();

// Register Email Sender Service
builder.Services.AddScoped<IEmailSender, Beloved.ControlPlane.Services.EmailSender>();
builder.Services.AddTransient<IMailClient, Beloved.ControlPlane.Services.LogMailClient>();

// Register Webhook Dispatcher
builder.Services.AddScoped<IWebhookDispatcher, Beloved.ControlPlane.Services.WebhookDispatcher>();
builder.Services.AddHttpClient("WebhookClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// -------------------------------------------------------------------------
// Pluggable LLM Provider Registration (Fowler/Edwards DI pattern)
// Controlled entirely by appsettings.json "Llm" section — no code changes
// needed to switch between Ollama, OpenAI, Gemini, or Claude.
// -------------------------------------------------------------------------
builder.Services.Configure<LlmProviderOptions>(builder.Configuration.GetSection(LlmProviderOptions.SectionName));

var llmOptions = builder.Configuration.GetSection(LlmProviderOptions.SectionName).Get<LlmProviderOptions>()
    ?? new LlmProviderOptions();

var providerName = llmOptions.Provider.Trim();

switch (providerName.ToLowerInvariant())
{
    case "openai":
        builder.Services.AddHttpClient<ILlmProvider, OpenAiLlmProvider>()
            .AddTypedClient<ILlmProvider>((client, sp) =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmProviderOptions>>().Value;
                return new OpenAiLlmProvider(client, opts.ApiKey, string.IsNullOrEmpty(opts.Model) ? "gpt-4o-mini" : opts.Model);
            });
        break;

    case "gemini":
        builder.Services.AddHttpClient<ILlmProvider, GeminiLlmProvider>()
            .AddTypedClient<ILlmProvider>((client, sp) =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmProviderOptions>>().Value;
                return new GeminiLlmProvider(client, opts.ApiKey, string.IsNullOrEmpty(opts.Model) ? "gemini-2.0-flash" : opts.Model);
            });
        break;

    case "claude":
        builder.Services.AddHttpClient<ILlmProvider, ClaudeLlmProvider>()
            .AddTypedClient<ILlmProvider>((client, sp) =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmProviderOptions>>().Value;
                return new ClaudeLlmProvider(client, opts.ApiKey, string.IsNullOrEmpty(opts.Model) ? "claude-3-haiku-20240307" : opts.Model);
            });
        break;

    case "ollama":
    default:
        var ollamaBase = string.IsNullOrEmpty(llmOptions.BaseUrl) ? "http://localhost:11434" : llmOptions.BaseUrl;
        var ollamaModel = string.IsNullOrEmpty(llmOptions.Model) ? "gpt-oss:120b-cloud" : llmOptions.Model;
        builder.Services.AddHttpClient<ILlmProvider, OllamaLlmProvider>(client =>
        {
            client.BaseAddress = new Uri(ollamaBase);
        })
        .AddTypedClient<ILlmProvider>((client, sp) =>
            new OllamaLlmProvider(client, ollamaModel));
        break;
}
// -------------------------------------------------------------------------

var app = builder.Build();

// Run EF Core Migrations and Seed Dummy Tenant
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();
    db.Database.Migrate();

    if (!db.Tenants.Any(t => t.Name == "DefaultTenant"))
    {
        db.Tenants.Add(new Beloved.ControlPlane.Models.Tenant
        {
            Name = "DefaultTenant",
            ApiKey = "beloved-dev-key"
        });
        db.SaveChanges();
    }
}

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// Quota gate — runs after auth so tenant is resolvable
app.UseMiddleware<Beloved.ControlPlane.Middleware.QuotaMiddleware>();

app.MapControllers();
app.MapHub<Beloved.ControlPlane.Hubs.AssemblyHub>("/assemblyhub");

// Serve default dashboard files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Serve compiled preview frontends from the output directory
var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
if (!Directory.Exists(outputDir))
{
    Directory.CreateDirectory(outputDir);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(outputDir),
    RequestPath = "/preview",
    OnPrepareResponse = ctx =>
    {
        // Disable caching for live previews
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Prometheus scraping endpoint: GET /metrics
app.MapPrometheusScrapingEndpoint();

// Health check endpoints for Kubernetes probes
app.MapGet("/healthz/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/healthz/ready", async (BelovedDbContext db) =>
{
    try
    {
        // Simple fast query to verify database is reachable
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "Ready" });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 503);
    }
});

// Map OpenAPI Endpoint
app.MapOpenApi("/openapi/v1.json");

app.Run();

