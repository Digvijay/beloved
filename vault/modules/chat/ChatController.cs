using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace BelovedApp.Controllers
{
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        [HttpPost]
        public IActionResult PostMessage([FromBody] ChatMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { message = "Message is required." });
            }

            var prompt = request.Message.ToLower();
            string reply;

            if (prompt.Contains("hello") || prompt.Contains("hi"))
            {
                reply = "Hello there! How can I help you build your application today?";
            }
            else if (prompt.Contains("help") || prompt.Contains("explain"))
            {
                reply = "I can guide you through managing data tables, handling authentication redirects, or configuring Stripe checkouts.";
            }
            else
            {
                reply = $"I received your message: \"{request.Message}\". As a simulated AI module, I am ready to process your production requests.";
            }

            return Ok(new { reply });
        }
    }

    public class ChatMessageRequest
    {
        [Required]
        public string Message { get; set; } = string.Empty;
    }
}
