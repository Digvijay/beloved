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
                    var parts = image.Split('/');
                    var imageTarget = parts[^1];
                    var imageParts = imageTarget.Split(':');
                    var moduleName = imageParts[0];
                    var tag = imageParts.Length > 1 ? imageParts[1] : "latest";

                    // Call lightweight signature verification (fails-closed on false)
                    var isValid = await _vaultRepository.VerifySignatureAsync(moduleName, tag);
                    if (!isValid)
                    {
                        _logger.LogWarning("Untrusted container signature rejected: {Image}", image);
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
