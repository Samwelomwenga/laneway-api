using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laneway.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTraceparent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Traceparent",
                table: "Deliveries",
                type: "varchar(55)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Traceparent",
                table: "CopyJobs",
                type: "varchar(55)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Traceparent",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "Traceparent",
                table: "CopyJobs");
        }
    }
}
