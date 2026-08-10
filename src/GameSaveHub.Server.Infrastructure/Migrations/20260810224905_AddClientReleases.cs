using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameSaveHub.Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Signature = table.Column<string>(type: "TEXT", nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientReleases_PublishedAtUtc",
                table: "ClientReleases",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ClientReleases_Version",
                table: "ClientReleases",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientReleases");
        }
    }
}
