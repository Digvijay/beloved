using Beloved.ControlPlane.Models;
using Microsoft.EntityFrameworkCore;

namespace Beloved.ControlPlane.Data
{
    public class BelovedDbContext : DbContext
    {
        public BelovedDbContext(DbContextOptions<BelovedDbContext> options) : base(options) { }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<AssemblyJob> AssemblyJobs { get; set; }
        public DbSet<Webhook> Webhooks { get; set; }
        public DbSet<AssemblyUsage> AssemblyUsages { get; set; }

        // ── Identity & Organisation ───────────────────────────────────────────
        public DbSet<BelovedUser> Users { get; set; }
        public DbSet<Organisation> Organisations { get; set; }
        public DbSet<OrganisationMember> OrganisationMembers { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<EmailQueueJob> EmailQueueJobs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.ApiKey)
                .IsUnique();

            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.Organisation)
                .WithMany(o => o.Tenants)
                .HasForeignKey(t => t.OrganisationId)
                .IsRequired(false);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.Tenant)
                .WithMany(t => t.Projects)
                .HasForeignKey(p => p.TenantId);

            modelBuilder.Entity<AssemblyJob>()
                .HasOne(j => j.Project)
                .WithMany(p => p.Jobs)
                .HasForeignKey(j => j.ProjectId);

            modelBuilder.Entity<Webhook>()
                .HasOne(w => w.Tenant)
                .WithMany()
                .HasForeignKey(w => w.TenantId);

            modelBuilder.Entity<Webhook>()
                .HasIndex(w => w.TenantId);

            // AssemblyUsage — FK to Tenant + composite index for fast monthly quota queries
            modelBuilder.Entity<AssemblyUsage>()
                .HasOne(u => u.Tenant)
                .WithMany(t => t.UsageRecords)
                .HasForeignKey(u => u.TenantId);

            modelBuilder.Entity<AssemblyUsage>()
                .HasIndex(u => new { u.TenantId, u.PeriodMonth });

            // ── BelovedUser ────────────────────────────────────────────────────
            // Unique index prevents duplicate OAuth logins (e.g. two GitHub accounts with same sub)
            modelBuilder.Entity<BelovedUser>()
                .HasIndex(u => new { u.Provider, u.ProviderSubject })
                .IsUnique();

            modelBuilder.Entity<BelovedUser>()
                .HasIndex(u => u.Email);

            // ── Organisation ───────────────────────────────────────────────────
            modelBuilder.Entity<Organisation>()
                .HasIndex(o => o.Slug)
                .IsUnique();

            // ── OrganisationMember ─────────────────────────────────────────────
            modelBuilder.Entity<OrganisationMember>()
                .HasOne(m => m.Organisation)
                .WithMany(o => o.Members)
                .HasForeignKey(m => m.OrganisationId);

            modelBuilder.Entity<OrganisationMember>()
                .HasOne(m => m.User)
                .WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserId);

            // One user can only be in each org once
            modelBuilder.Entity<OrganisationMember>()
                .HasIndex(m => new { m.OrganisationId, m.UserId })
                .IsUnique();

            // ── RefreshToken ───────────────────────────────────────────────────
            modelBuilder.Entity<RefreshToken>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId);

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(r => r.Token)
                .IsUnique();
        }
    }
}
