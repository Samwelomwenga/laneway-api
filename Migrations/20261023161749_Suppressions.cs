using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laneway.Api.Migrations
{
    /// <inheritdoc />
    public partial class Suppressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Suppressions",
                columns: table => new
                {
                    AddressHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    SuppressedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MessageId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppressions", x => new { x.AddressHash, x.Kind });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_ProviderMessageId",
                table: "Deliveries",
                column: "ProviderMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Suppressions");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_ProviderMessageId",
                table: "Deliveries");
        }
    }
}
