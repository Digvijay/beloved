using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;

namespace BelovedApp.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class EntraIdAuthController : ControllerBase
    {
        [HttpPost("entraid")]
        public IActionResult EntraIdLogin([FromBody] EntraIdLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return BadRequest(new { message = "Tenant ID is required." });
            }

            // Simulate tenant token validation
            var domain = request.TenantId.Split('.')[0];

            return Ok(new
            {
                token = "entraid_simulated_token_for_tenant_" + domain,
                user = new
                {
                    username = "User@" + (domain.Contains("-") ? domain : domain + ".onmicrosoft.com")
                }
            });
        }
    }

    public class EntraIdLoginRequest
    {
        [Required]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
