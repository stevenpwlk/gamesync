using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameSaveHub.Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedByPlayerName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublishedByPlayerName",
                table: "SaveVersions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedByPlayerName",
                table: "SaveVersions");
        }
    }
}
