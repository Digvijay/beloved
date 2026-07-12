using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Beloved.AssemblyEngine;
using Beloved.ControlPlane.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Microsoft.AspNetCore.Authorization;

namespace Beloved.ControlPlane.Controllers;

[ApiController]
[Route("api")]
public class ControlPlaneController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IVaultRepository _vaultRepository;
    private readonly AssemblyCompiler _compiler;
    private readonly ILlmProvider _llmProvider;
    private readonly IAssemblyQueue _queue;
    private readonly IOutputStore _outputStore;
    private readonly SandboxOrchestrator _sandboxOrchestrator;
    private readonly BelovedDbContext _db;

    public ControlPlaneController(IWebHostEnvironment env, IVaultRepository vaultRepository, AssemblyCompiler compiler, ILlmProvider llmProvider, IAssemblyQueue queue, IOutputStore outputStore, SandboxOrchestrator sandboxOrchestrator, BelovedDbContext db)
    {
        _env = env;
        _vaultRepository = vaultRepository;
        _compiler = compiler;
        _llmProvider = llmProvider;
        _queue = queue;
        _outputStore = outputStore;
        _sandboxOrchestrator = sandboxOrchestrator;
        _db = db;
    }

    [HttpGet("modules")]
    public async Task<IActionResult> GetModules()
    {
        var modules = await _vaultRepository.ListModulesAsync();
        return Ok(modules);
    }

    [HttpPost("modules/publish")]
    [Authorize]
    public async Task<IActionResult> PublishModule(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("File is empty");

        var tempWorkspace = Path.Combine(Path.GetTempPath(), "beloved_publish_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempWorkspace);

        try
        {
            var zipPath = Path.Combine(tempWorkspace, "module.zip");
            using (var stream = new FileStream(zipPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var extractPath = Path.Combine(tempWorkspace, "extracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);

            var manifestPath = Path.Combine(extractPath, "manifest.json");
            if (!System.IO.File.Exists(manifestPath)) return BadRequest("manifest.json missing from root");

            var manifestContent = await System.IO.File.ReadAllTextAsync(manifestPath);
            var manifest = System.Text.Json.JsonSerializer.Deserialize(manifestContent, AssemblyJsonContext.Default.ModuleManifest);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Name)) return BadRequest("Invalid manifest: Name is required");

            var modName = manifest.Name.ToLower();

            // Create Dockerfile
            var dockerfileContent = "FROM scratch\nCOPY . /\n";
            await System.IO.File.WriteAllTextAsync(Path.Combine(extractPath, "Dockerfile"), dockerfileContent);

            var registry = "localhost:5001";
            var tag = $"{registry}/modules/{modName}:latest";

            // Docker build
            var buildInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"build -t {tag} .",
                WorkingDirectory = extractPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var buildProcess = Process.Start(buildInfo);
            await buildProcess!.WaitForExitAsync();
            if (buildProcess.ExitCode != 0) return StatusCode(500, "Docker build failed: " + await buildProcess.StandardError.ReadToEndAsync());

            // Docker push
            var pushInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"push {tag}",
                WorkingDirectory = extractPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var pushProcess = Process.Start(pushInfo);
            await pushProcess!.WaitForExitAsync();
            if (pushProcess.ExitCode != 0) return StatusCode(500, "Docker push failed: " + await pushProcess.StandardError.ReadToEndAsync());

            // Cosign sign
            var cosignInfo = new ProcessStartInfo
            {
                FileName = "cosign",
                Arguments = $"sign --key /Users/digvijay/github/beloved/cosign-key.key --signing-config /Users/digvijay/github/beloved/offline-config.json --allow-http-registry {tag}",
                WorkingDirectory = extractPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            cosignInfo.EnvironmentVariables["COSIGN_PASSWORD"] = "";
            var cosignProcess = Process.Start(cosignInfo);
            await cosignProcess!.WaitForExitAsync();
            if (cosignProcess.ExitCode != 0) return StatusCode(500, "Cosign signing failed: " + await cosignProcess.StandardError.ReadToEndAsync());

            return Ok(new { message = $"Module {manifest.Name} published and signed successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempWorkspace)) Directory.Delete(tempWorkspace, true);
        }
    }

    [HttpGet("jobs/{jobId}/sbom")]
    [Authorize]
    public async Task<IActionResult> GetJobSbom(string jobId)
    {
        var tenantIdStr = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Unauthorized();

        var job = await _db.AssemblyJobs.Include(j => j.Project).FirstOrDefaultAsync(j => j.QueueJobId == jobId && j.Project != null && j.Project.TenantId == tenantId);
        if (job == null) return NotFound("Job not found or access denied.");
        if (string.IsNullOrEmpty(job.SbomJson)) return NotFound("SBOM not available for this job.");

        return Content(job.SbomJson, "application/json");
    }

    // ── Webhook Management ─────────────────────────────────────────────────

    public class RegisterWebhookRequest
    {
        public string Url { get; set; } = string.Empty;
        public string Events { get; set; } = string.Empty; // comma-separated, empty = all
        public string Secret { get; set; } = string.Empty;
    }

    [HttpPost("webhooks")]
    [Authorize]
    public async Task<IActionResult> RegisterWebhook([FromBody] RegisterWebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url)) return BadRequest("Url is required.");
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _)) return BadRequest("Url must be a valid absolute URI.");

        var tenantIdStr = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Unauthorized();

        var webhook = new Models.Webhook
        {
            TenantId = tenantId,
            Url = request.Url,
            Events = request.Events,
            Secret = request.Secret
        };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        return Ok(new { webhook.Id, webhook.Url, webhook.Events, webhook.IsActive, webhook.CreatedAt });
    }

    [HttpGet("webhooks")]
    [Authorize]
    public async Task<IActionResult> ListWebhooks()
    {
        var tenantIdStr = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Unauthorized();

        var webhooks = await _db.Webhooks
            .Where(w => w.TenantId == tenantId)
            .Select(w => new { w.Id, w.Url, w.Events, w.IsActive, w.CreatedAt })
            .ToListAsync();

        return Ok(webhooks);
    }

    [HttpDelete("webhooks/{webhookId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteWebhook(Guid webhookId)
    {
        var tenantIdStr = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Unauthorized();

        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == webhookId && w.TenantId == tenantId);
        if (webhook == null) return NotFound();

        _db.Webhooks.Remove(webhook);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ──────────────────────────────────────────────────────────────────────

    public class IntentRequest
    {
        public string Prompt { get; set; } = string.Empty;
    }

    [HttpPost("intent")]
    public async Task<IActionResult> MapIntent([FromBody] IntentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt)) return BadRequest("Prompt is required");

        // 1. Get available modules from vault
        var modules = await _vaultRepository.ListModulesAsync();

        // 2. Ask the IntentMapper (LLM) to map it to a Blueprint
        try
        {
            var blueprint = await _llmProvider.MapIntentAsync(request.Prompt, modules);
            if (blueprint == null) return StatusCode(500, "Failed to generate blueprint");
            return Ok(blueprint);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    public class RefineBlueprintRequest
    {
        public Blueprint CurrentBlueprint { get; set; } = null!;
        public string RefinePrompt { get; set; } = string.Empty;
    }

    [HttpPost("blueprints/refine")]
    public async Task<IActionResult> RefineBlueprint([FromBody] RefineBlueprintRequest request)
    {
        if (request.CurrentBlueprint == null) return BadRequest("CurrentBlueprint is required");
        if (string.IsNullOrWhiteSpace(request.RefinePrompt)) return BadRequest("RefinePrompt is required");

        var modules = await _vaultRepository.ListModulesAsync();

        try
        {
            var updatedBlueprint = await _llmProvider.RefineBlueprintAsync(request.CurrentBlueprint, request.RefinePrompt, modules);
            if (updatedBlueprint == null) return StatusCode(500, "Failed to refine blueprint");
            return Ok(updatedBlueprint);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    public class AssembleRequest
    {
        public required string ProjectId { get; set; }
        public required JsonElement Blueprint { get; set; }
    }

    [HttpPost("assemble")]
    [Authorize]
    public async Task<IActionResult> Assemble([FromBody] AssembleRequest request, [FromServices] Beloved.ControlPlane.Data.BelovedDbContext dbContext)
    {
        try
        {
            var blueprintStr = request.Blueprint.GetRawText();
            var blueprint = JsonSerializer.Deserialize(blueprintStr, AssemblyJsonContext.Default.Blueprint);
            if (blueprint == null) return BadRequest("Invalid blueprint");

            var projectId = Guid.Parse(request.ProjectId);
            var tenantIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var tenantId = Guid.Parse(tenantIdClaim!);

            // Ensure project belongs to tenant
            var project = await dbContext.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
            if (project == null) return NotFound("Project not found.");

            var queueJobId = Guid.NewGuid().ToString("N");
            
            var jobEntity = new Beloved.ControlPlane.Models.AssemblyJob
            {
                ProjectId = projectId,
                QueueJobId = queueJobId,
                Status = "Queued",
                BlueprintJson = blueprintStr
            };
            
            dbContext.AssemblyJobs.Add(jobEntity);
            await dbContext.SaveChangesAsync();

            var job = new Beloved.ControlPlane.Services.AssemblyJob(queueJobId, blueprint);
            await _queue.QueueJobAsync(job);

            return Accepted(new { JobId = queueJobId, DatabaseJobId = jobEntity.Id, Message = "Assembly queued successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("artifacts/{jobId}")]
    public async Task<IActionResult> DownloadArtifact(string jobId)
    {
        try
        {
            var stream = await _outputStore.GetArtifactAsync(jobId);
            if (stream == null) return NotFound("Artifact not found or expired.");

            return File(stream, "application/zip", $"{jobId}.zip");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    public class PreviewRequest
    {
        public string JobId { get; set; } = string.Empty;
    }

    [HttpPost("preview/start")]
    public async Task<IActionResult> StartPreview([FromBody] PreviewRequest request)
    {
        try
        {
            var result = await _sandboxOrchestrator.StartSandboxAsync(request.JobId);
            if (result.success)
            {
                return Ok(new { message = "Preview started", url = result.url });
            }
            return StatusCode(500, new { error = result.error });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("preview/stop")]
    public async Task<IActionResult> StopPreview()
    {
        try
        {
            await _sandboxOrchestrator.StopSandboxAsync();
            return Ok(new { message = "Preview stopped" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
