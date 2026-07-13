using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BelovedApp.Controllers
{
    [ApiController]
    [Route("api/tasks")]
    public class TasksController : ControllerBase
    {
        private static readonly ConcurrentDictionary<int, UserTask> _tasks = new();
        private static int _nextId = 1;

        static TasksController()
        {
            _tasks.TryAdd(_nextId++, new UserTask { Id = 1, Title = "Design system architecture blueprint", Status = "Todo" });
            _tasks.TryAdd(_nextId++, new UserTask { Id = 2, Title = "Integrate Stripe subscription payment flows", Status = "InProgress" });
            _tasks.TryAdd(_nextId++, new UserTask { Id = 3, Title = "Implement OpenTelemetry span tracing", Status = "Done" });
        }

        [HttpGet]
        public IActionResult GetTasks()
        {
            return Ok(_tasks.Values.ToList());
        }

        [HttpPost]
        public IActionResult CreateTask([FromBody] CreateTaskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { message = "Task title is required." });
            }

            var task = new UserTask
            {
                Id = _nextId++,
                Title = request.Title,
                Status = request.Status ?? "Todo"
            };

            _tasks[task.Id] = task;
            return Ok(task);
        }

        [HttpPut("{id}")]
        public IActionResult UpdateTask(int id, [FromBody] UpdateTaskRequest request)
        {
            if (!_tasks.TryGetValue(id, out var task))
            {
                return NotFound();
            }

            if (!string.IsNullOrEmpty(request.Status))
            {
                task.Status = request.Status;
            }

            return Ok(task);
        }
    }

    public class UserTask
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = "Todo"; // Todo, InProgress, Done
    }

    public class CreateTaskRequest
    {
        [Required]
        public string Title { get; set; } = string.Empty;
        public string? Status { get; set; }
    }

    public class UpdateTaskRequest
    {
        public string? Status { get; set; }
    }
}
