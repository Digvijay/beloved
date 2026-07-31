using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beloved.ControlPlane.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_SBOM : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SbomJson",
                table: "AssemblyJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SbomJson",
                table: "AssemblyJobs");
        }
    }
}
