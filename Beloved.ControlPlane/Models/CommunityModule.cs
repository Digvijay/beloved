using System;

namespace Beloved.ControlPlane.Models
{
    public enum ModuleVerificationStatus
    {
        Pending = 0,
        Verified = 1,
        Rejected = 2
    }

    /// <summary>
    /// Represents a community-submitted component module in the Beloved Vault.
    /// Stores OCI image references, signature verification status, and metadata.
    /// </summary>
    public class CommunityModule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public required string Version { get; set; } = "1.0.0";
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string AuthorName { get; set; } = "Community Contributor";
        public string AuthorEmail { get; set; } = string.Empty;
        
        public Guid? PublisherTenantId { get; set; }
        public Tenant? PublisherTenant { get; set; }

        public string OciTag { get; set; } = string.Empty;
        public string OciDigest { get; set; } = string.Empty;

        public ModuleVerificationStatus Status { get; set; } = ModuleVerificationStatus.Pending;
        public bool IsVerified { get; set; } = false;
        public string VerificationLog { get; set; } = string.Empty;

        public int DownloadsCount { get; set; } = 0;
        public bool IsUnpublished { get; set; } = false;
        public DateTime? UnpublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
