using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelovedApp.Controllers;

[Authorize]
[ApiController]
[Route("api/items")]
public class ItemsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ItemsController(AppDbContext context)
    {
        _context = context;
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var userId = GetUserId();
        var items = _context.Items.Where(i => i.UserId == userId).ToList();
        return Ok(items);
    }

    [HttpPost]
    public IActionResult Create([FromBody] ItemRequest request)
    {
        var userId = GetUserId();
        var item = new Item
        {
            UserId = userId,
            Name = request.Name,
            Description = request.Description,
            Quantity = request.Quantity
        };

        _context.Items.Add(item);
        _context.SaveChanges();

        return CreatedAtAction(nameof(GetAll), new { id = item.Id }, item);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var userId = GetUserId();
        var item = _context.Items.FirstOrDefault(i => i.Id == id && i.UserId == userId);
        if (item == null) return NotFound(new { message = "Item not found or unauthorized" });

        _context.Items.Remove(item);
        _context.SaveChanges();
        return Ok(new { message = "Item deleted successfully" });
    }
}

public class Item
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}
