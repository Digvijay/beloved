using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beloved.ControlPlane.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_CommunityModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommunityModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorEmail = table.Column<string>(type: "TEXT", nullable: false),
                    PublisherTenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OciTag = table.Column<string>(type: "TEXT", nullable: false),
                    OciDigest = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    VerificationLog = table.Column<string>(type: "TEXT", nullable: false),
                    DownloadsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityModules_Tenants_PublisherTenantId",
                        column: x => x.PublisherTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityModules_Category",
                table: "CommunityModules",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityModules_IsVerified",
                table: "CommunityModules",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityModules_Name_Version",
                table: "CommunityModules",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityModules_PublisherTenantId",
                table: "CommunityModules",
                column: "PublisherTenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunityModules");
        }
    }
}
