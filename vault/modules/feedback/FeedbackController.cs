using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BelovedApp.Controllers
{
    [ApiController]
    [Route("api/feedback")]
    public class FeedbackController : ControllerBase
    {
        private static readonly ConcurrentBag<FeedbackItem> _feedbackItems = new();

        [HttpPost]
        public IActionResult SubmitFeedback([FromBody] FeedbackSubmission request)
        {
            if (string.IsNullOrWhiteSpace(request.Comment))
            {
                return BadRequest(new { message = "Comment description is required." });
            }

            var item = new FeedbackItem
            {
                Category = request.Category ?? "Idea",
                Comment = request.Comment,
                Rating = request.Rating
            };

            _feedbackItems.Add(item);
            return Ok(new { message = "Feedback submitted successfully." });
        }

        [HttpGet]
        public IActionResult GetFeedback()
        {
            return Ok(_feedbackItems.ToList());
        }
    }

    public class FeedbackItem
    {
        public string Category { get; set; } = "Idea";
        public string Comment { get; set; } = string.Empty;
        public int Rating { get; set; } = 5;
    }

    public class FeedbackSubmission
    {
        public string? Category { get; set; }

        [Required]
        public string Comment { get; set; } = string.Empty;

        [Range(1, 5)]
        public int Rating { get; set; } = 5;
    }
}
