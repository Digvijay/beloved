using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;

namespace BelovedApp.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class GithubAuthController : ControllerBase
    {
        [HttpPost("github")]
        public IActionResult GithubLogin([FromBody] GithubLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new { message = "OAuth code is required." });
            }

            // Simulate parsing and exchange code with GitHub API for profile username
            var mockUsername = "gituser-" + request.Code.Substring(0, Math.Min(6, request.Code.Length));

            return Ok(new
            {
                token = "github_oauth_token_for_" + mockUsername,
                user = new
                {
                    username = mockUsername + " (GitHub)"
                }
            });
        }
    }

    public class GithubLoginRequest
    {
        [Required]
        public string Code { get; set; } = string.Empty;
    }
}
