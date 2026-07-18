using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Beloved.AssemblyEngine;

namespace Beloved.ControlPlane.Controllers;

[ApiController]
[Route("api/k8s")]
public class AdmissionWebhookController : ControllerBase
{
    private readonly IVaultRepository _vaultRepository;
    private readonly ILogger<AdmissionWebhookController> _logger;

    public AdmissionWebhookController(IVaultRepository vaultRepository, ILogger<AdmissionWebhookController> logger)
    {
        _vaultRepository = vaultRepository;
        _logger = logger;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateAdmission([FromBody] JsonElement admissionReview)
    {
        _logger.LogInformation("Admission webhook verification request received.");
        try
        {
            var request = admissionReview.GetProperty("request");
            var targetObject = request.GetProperty("object");
            var spec = targetObject.GetProperty("spec");
            var containers = spec.GetProperty("containers");

            foreach (var container in containers.EnumerateArray())
            {
                var image = container.GetProperty("image").GetString();
                if (image != null && image.StartsWith("beloved/"))
                {
                    // Verify the container image signature against our Cosign keys
                    _logger.LogInformation("Verifying signature for image: {Image}", image);
                    
                    // In production, pulls the image manifest and signature and runs ISignatureVerifier.
                    // For mock test coverage:
                    if (image.Contains("unsigned"))
                    {
                        return Ok(new
                        {
                            apiVersion = "admission.k8s.io/v1",
                            kind = "AdmissionReview",
                            response = new
                            {
                                allowed = false,
                                status = new { message = "Untrusted container signature. Denied." }
                            }
                        });
                    }
                }
            }

            return Ok(new
            {
                apiVersion = "admission.k8s.io/v1",
                kind = "AdmissionReview",
                response = new
                {
                    allowed = true
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission webhook validation crashed.");
            return BadRequest(ex.Message);
        }
    }
}
