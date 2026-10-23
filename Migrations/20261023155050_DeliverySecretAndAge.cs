using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laneway.Api.Migrations
{
    /// <inheritdoc />
    public partial class DeliverySecretAndAge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Secret",
                table: "Deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_CreatedAt",
                table: "Deliveries",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deliveries_CreatedAt",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "Secret",
                table: "Deliveries");
        }
    }
}
