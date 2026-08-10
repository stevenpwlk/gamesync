using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameSaveHub.Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionPlayerName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlayerName",
                table: "Sessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayerName",
                table: "Sessions");
        }
    }
}
