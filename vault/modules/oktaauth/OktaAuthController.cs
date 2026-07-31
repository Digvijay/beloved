using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;

namespace BelovedApp.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class OktaAuthController : ControllerBase
    {
        [HttpPost("okta")]
        public IActionResult OktaLogin([FromBody] OktaLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@"))
            {
                return BadRequest(new { message = "Invalid corporate email address." });
            }

            // Simulate parsing and validating Okta id_token
            var username = request.Email.Split('@')[0];
            
            // Return simulated JWT token and user info matching application shell structure
            return Ok(new
            {
                token = "okta_simulated_jwt_token_for_" + username,
                user = new
                {
                    username = username + " (Okta SSO)"
                }
            });
        }
    }

    public class OktaLoginRequest
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
