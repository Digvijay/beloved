using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Controllers;

/// <summary>
/// Organisation management endpoints:
///   POST   /api/orgs                     — create organisation
///   GET    /api/orgs                     — list orgs for the current user
///   GET    /api/orgs/{slug}              — org details + members
///   POST   /api/orgs/{slug}/members      — invite user by email
///   DELETE /api/orgs/{slug}/members/{id} — remove member
///   GET    /api/orgs/{slug}/usage        — per-org quota rollup
/// </summary>
[ApiController]
[Route("api/orgs")]
[Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = "JwtBearer")]
public sealed class OrgsController : ControllerBase
{
    private readonly BelovedDbContext _db;

    public OrgsController(BelovedDbContext db) => _db = db;

    // ── POST /api/orgs ────────────────────────────────────────────────────────

    public sealed record CreateOrgRequest(string Name, string? Slug);

    [HttpPost]
    public async Task<IActionResult> CreateOrg([FromBody] CreateOrgRequest req, CancellationToken ct)
    {
        var user = await ResolveUserAsync(ct);
        if (user == null) return Unauthorized();

        // Auto-generate slug from name if not provided
        var slug = string.IsNullOrWhiteSpace(req.Slug)
            ? Regex.Replace(req.Name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-')
            : req.Slug.ToLowerInvariant();

        if (await _db.Organisations.AnyAsync(o => o.Slug == slug, ct))
            return Conflict($"Organisation slug '{slug}' is already taken.");

        var org = new Organisation { Name = req.Name, Slug = slug };
        _db.Organisations.Add(org);

        // Creator becomes Owner automatically
        _db.OrganisationMembers.Add(new OrganisationMember
        {
            OrganisationId = org.Id,
            UserId         = user.Id,
            Role           = OrgRole.Owner
        });

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetOrg), new { slug }, new { org.Id, org.Name, org.Slug, org.CreatedAt });
    }

    // ── GET /api/orgs ─────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ListOrgs(CancellationToken ct)
    {
        var user = await ResolveUserAsync(ct);
        if (user == null) return Unauthorized();

        var orgs = await _db.OrganisationMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .Select(m => new
            {
                m.Organisation!.Id,
                m.Organisation.Name,
                m.Organisation.Slug,
                m.Role,
                m.JoinedAt
            })
            .ToListAsync(ct);

        return Ok(orgs);
    }

    // ── GET /api/orgs/{slug} ──────────────────────────────────────────────────

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetOrg(string slug, CancellationToken ct)
    {
        var user = await ResolveUserAsync(ct);
        if (user == null) return Unauthorized();

        var org = await _db.Organisations
            .AsNoTracking()
            .Include(o => o.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

        if (org == null) return NotFound();

        // Must be a member to see org details
        if (!org.Members.Any(m => m.UserId == user.Id)) return Forbid();

        return Ok(new
        {
            org.Id,
            org.Name,
            org.Slug,
            org.CreatedAt,
            members = org.Members.Select(m => new
            {
                m.Id,
                m.Role,
                m.JoinedAt,
                user = new { m.User!.Email, m.User.DisplayName, m.User.AvatarUrl }
            })
        });
    }

    // ── POST /api/orgs/{slug}/members ─────────────────────────────────────────

    public sealed record InviteMemberRequest(string Email, OrgRole Role);

    [HttpPost("{slug}/members")]
    public async Task<IActionResult> InviteMember(string slug, [FromBody] InviteMemberRequest req, CancellationToken ct)
    {
        var actor = await ResolveUserAsync(ct);
        if (actor == null) return Unauthorized();

        var org = await _db.Organisations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

        if (org == null) return NotFound();

        // Only Owner or Admin can invite
        var actorRole = org.Members.FirstOrDefault(m => m.UserId == actor.Id)?.Role;
        if (actorRole is not (OrgRole.Owner or OrgRole.Admin)) return Forbid();

        var invitee = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (invitee == null)
            return NotFound($"No Beloved user with email '{req.Email}'. They must log in first.");

        if (org.Members.Any(m => m.UserId == invitee.Id))
            return Conflict("User is already a member of this organisation.");

        _db.OrganisationMembers.Add(new OrganisationMember
        {
            OrganisationId = org.Id,
            UserId         = invitee.Id,
            Role           = req.Role
        });
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"{req.Email} added as {req.Role}" });
    }

    // ── DELETE /api/orgs/{slug}/members/{memberId} ────────────────────────────

    [HttpDelete("{slug}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(string slug, Guid memberId, CancellationToken ct)
    {
        var actor = await ResolveUserAsync(ct);
        if (actor == null) return Unauthorized();

        var org = await _db.Organisations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

        if (org == null) return NotFound();

        var actorRole = org.Members.FirstOrDefault(m => m.UserId == actor.Id)?.Role;
        if (actorRole is not (OrgRole.Owner or OrgRole.Admin)) return Forbid();

        var member = org.Members.FirstOrDefault(m => m.Id == memberId);
        if (member == null) return NotFound();
        if (member.Role == OrgRole.Owner) return BadRequest("Cannot remove the org Owner.");

        _db.OrganisationMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── GET /api/orgs/{slug}/usage ────────────────────────────────────────────

    /// <summary>Per-org assembly quota rollup — total across all member tenants.</summary>
    [HttpGet("{slug}/usage")]
    public async Task<IActionResult> GetOrgUsage(string slug, CancellationToken ct)
    {
        var user = await ResolveUserAsync(ct);
        if (user == null) return Unauthorized();

        var org = await _db.Organisations
            .AsNoTracking()
            .Include(o => o.Members)
            .Include(o => o.Tenants)
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

        if (org == null) return NotFound();
        if (!org.Members.Any(m => m.UserId == user.Id)) return Forbid();

        var period = DateTime.UtcNow.ToString("yyyy-MM");
        var tenantIds = org.Tenants.Select(t => t.Id).ToList();

        var totalAssemblies = await _db.AssemblyUsages
            .AsNoTracking()
            .CountAsync(u => tenantIds.Contains(u.TenantId) && u.PeriodMonth == period, ct);

        var breakdown = await _db.AssemblyUsages
            .AsNoTracking()
            .Where(u => tenantIds.Contains(u.TenantId) && u.PeriodMonth == period)
            .GroupBy(u => u.TenantId)
            .Select(g => new { tenantId = g.Key, count = g.Count() })
            .ToListAsync(ct);

        return Ok(new { org = slug, period, totalAssemblies, breakdown });
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task<BelovedUser?> ResolveUserAsync(CancellationToken ct)
    {
        // Try JWT sub claim first (OAuth2 login)
        var subClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (Guid.TryParse(subClaim, out var userId))
            return await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        // Fallback: API key → return null (org routes require full identity)
        return null;
    }
}
