using System.ComponentModel.DataAnnotations;

namespace Beloved.ControlPlane.Options
{
    public class OciVaultOptions
    {
        public const string SectionName = "OciVault";

        [Required]
        public string RegistryUrl { get; set; } = "http://localhost:5001";

        [Range(1, 300)]
        public int TimeoutSeconds { get; set; } = 30;

        public bool EnableHttp2 { get; set; } = true;

        [Range(1, 1000)]
        public int MaxConnectionsPerServer { get; set; } = 50;
    }

    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        [Required]
        [MinLength(16)]
        public string Secret { get; set; } = "beloved-dev-secret-change-in-production";

        [Required]
        public string Issuer { get; set; } = "beloved.dev";

        /// <summary>Expected JWT Audience.</summary>
        public string Audience { get; set; } = "beloved.dev";

        [Range(1, 43200)]
        public int ExpiryMinutes { get; set; } = 1440;
    }

    public class AssemblyEngineOptions
    {
        public const string SectionName = "AssemblyEngine";

        public string TempWorkspacePath { get; set; } = "beloved_temp";

        [Range(1, 100)]
        public int MaxConcurrentAssemblies { get; set; } = 10;
    }
}
