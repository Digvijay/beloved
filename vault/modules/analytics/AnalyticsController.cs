using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BelovedApp.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly DbContext _db;

    public AnalyticsController(DbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents()
    {
        // Dynamically query database table using reflection or direct Set access
        var set = _db.Set<AnalyticsEvent>();
        var items = await set.OrderByDescending(x => x.RecordedAt).ToListAsync();
        return Ok(items);
    }
}

public class AnalyticsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MetricName { get; set; } = string.Empty;
    public string MetricValue { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
