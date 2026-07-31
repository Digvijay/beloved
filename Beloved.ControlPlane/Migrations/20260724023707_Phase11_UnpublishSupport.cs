using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beloved.ControlPlane.Migrations
{
    /// <inheritdoc />
    public partial class Phase11_UnpublishSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsUnpublished",
                table: "CommunityModules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnpublishedAt",
                table: "CommunityModules",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsUnpublished",
                table: "CommunityModules");

            migrationBuilder.DropColumn(
                name: "UnpublishedAt",
                table: "CommunityModules");
        }
    }
}
