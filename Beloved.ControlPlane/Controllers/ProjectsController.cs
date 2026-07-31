using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beloved.ControlPlane.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly BelovedDbContext _dbContext;

    public ProjectsController(BelovedDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private Guid GetTenantId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(claim!);
    }

    [HttpGet]
    public async Task<IActionResult> ListProjects()
    {
        var tenantId = GetTenantId();
        var projects = await _dbContext.Projects
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Name, p.CreatedAt })
            .ToListAsync();

        return Ok(projects);
    }

    public class CreateProjectRequest
    {
        public required string Name { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
    {
        var tenantId = GetTenantId();
        
        var project = new Project
        {
            TenantId = tenantId,
            Name = request.Name
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(ListProjects), new { id = project.Id }, new { project.Id, project.Name, project.CreatedAt });
    }
}
