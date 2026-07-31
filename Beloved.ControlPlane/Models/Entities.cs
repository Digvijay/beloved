using System;
using System.Collections.Generic;

namespace Beloved.ControlPlane.Models
{
    // ══════════════════════════════════════════════════════════════════════════
    // Identity & Organisation Model
    // ══════════════════════════════════════════════════════════════════════════


    /// <summary>
    /// A human user authenticated via OAuth2 (GitHub / Google) or SAML.
    /// One user can belong to multiple organisations.
    /// </summary>
    public class BelovedUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>OAuth2 provider name: "github" | "google" | "saml".</summary>
        public required string Provider { get; set; }

        /// <summary>The subject claim from the IdP (GitHub user ID, Google sub, SAML nameId).</summary>
        public required string ProviderSubject { get; set; }

        public required string Email { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

        public ICollection<OrganisationMember> Memberships { get; set; } = new List<OrganisationMember>();
    }

    /// <summary>Roles within an organisation — determines what a member can do.</summary>
    public enum OrgRole
    {
        /// <summary>Full control — billing, member management, delete org.</summary>
        Owner = 0,
        /// <summary>Can manage members and projects; cannot touch billing.</summary>
        Admin = 1,
        /// <summary>Can create projects and trigger assemblies.</summary>
        Developer = 2,
        /// <summary>Read-only access to project status and artifacts.</summary>
        Viewer = 3
    }

    /// <summary>
    /// An organisation groups multiple tenants and users under shared billing.
    /// One org may have many tenants (e.g. dev/staging/prod).
    /// </summary>
    public class Organisation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public required string Slug { get; set; } // URL-safe identifier
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<OrganisationMember> Members { get; set; } = new List<OrganisationMember>();
        public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
    }

    /// <summary>Join table — user ↔ organisation with role assignment.</summary>
    public class OrganisationMember
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrganisationId { get; set; }
        public Guid UserId { get; set; }
        public OrgRole Role { get; set; } = OrgRole.Developer;
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public Organisation? Organisation { get; set; }
        public BelovedUser? User { get; set; }
    }


    /// <summary>Billing plan controlling assembly quota per calendar month.</summary>
    public enum TenantPlan
    {
        /// <summary>50 assemblies / month — free tier.</summary>
        Free = 0,
        /// <summary>500 assemblies / month — $49/mo.</summary>
        Pro = 1,
        /// <summary>Unlimited assemblies — custom enterprise pricing.</summary>
        Enterprise = 2
    }

    public class Tenant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public required string ApiKey { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Billing ────────────────────────────────────────────────────────────
        public TenantPlan Plan { get; set; } = TenantPlan.Free;
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }

        // ── Organisation ──────────────────────────────────────────────────────
        public Guid? OrganisationId { get; set; }
        public Organisation? Organisation { get; set; }

        /// <summary>Max assemblies allowed per calendar month. Derived from Plan.</summary>
        public int MonthlyQuota => Plan switch
        {
            TenantPlan.Pro        => 500,
            TenantPlan.Enterprise => int.MaxValue,
            _                     => 50    // Free
        };

        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public ICollection<AssemblyUsage> UsageRecords { get; set; } = new List<AssemblyUsage>();
    }

    public class Project
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public required string Name { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Tenant? Tenant { get; set; }
        public ICollection<AssemblyJob> Jobs { get; set; } = new List<AssemblyJob>();
    }

    public class AssemblyJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProjectId { get; set; }
        
        // This links the relational model to the ephemeral string JobId used in the worker queue
        public required string QueueJobId { get; set; } 
        
        public required string Status { get; set; } // Queued, Assembling, Completed, Failed
        public required string BlueprintJson { get; set; }
        public string SbomJson { get; set; } = string.Empty; // Software Bill of Materials
        public string? ArtifactUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        public Project? Project { get; set; }
    }

    /// <summary>
    /// A tenant-registered HTTP endpoint that receives assembly lifecycle events.
    /// Events: job.queued | job.completed | job.failed
    /// </summary>
    public class Webhook
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }

        /// <summary>The HTTPS endpoint to POST the event payload to.</summary>
        public required string Url { get; set; }

        /// <summary>Optional comma-separated event filter. Empty = all events.</summary>
        public string Events { get; set; } = string.Empty;

        /// <summary>HMAC-SHA256 secret for payload signature (X-Beloved-Signature header).</summary>
        public string Secret { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Tenant? Tenant { get; set; }
    }

    /// <summary>
    /// Records a single assembly's usage for billing metering.
    /// One record is written per completed or failed job.
    /// </summary>
    public class AssemblyUsage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }

        /// <summary>Links back to the AssemblyJob.QueueJobId.</summary>
        public required string JobId { get; set; }

        /// <summary>Wall-clock time the assembly took in milliseconds.</summary>
        public long DurationMs { get; set; }

        /// <summary>Number of OCI modules pulled during this assembly.</summary>
        public int ModuleCount { get; set; }

        /// <summary>Whether the job completed successfully.</summary>
        public bool Succeeded { get; set; }

        /// <summary>ISO-8601 billing period key: "YYYY-MM" (e.g. "2026-07").</summary>
        public required string PeriodMonth { get; set; }

        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        public Tenant? Tenant { get; set; }
    }

    /// <summary>
    /// Persisted OAuth2 refresh token. Rotated on every use (old token revoked, new issued).
    /// Lifetime: 30 days. Stored hashed in production — plaintext only in dev.
    /// </summary>
    public class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public required string Token { get; set; }
        public bool IsRevoked { get; set; } = false;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public BelovedUser? User { get; set; }
    }

    /// <summary>
    /// Transactional Outbox pattern for emails. Emails are queued to the DB first,
    /// then processed asynchronously with retries by a background service.
    /// </summary>
    public class EmailQueueJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string RecipientEmail { get; set; }
        public required string Subject { get; set; }
        public required string Body { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Sent, Failed
        public int RetryCount { get; set; } = 0;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
    }
}

