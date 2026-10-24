using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laneway.Api.Migrations
{
    /// <inheritdoc />
    public partial class AuthTokenEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "AuthTokens",
                type: "varchar(255)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "AuthTokens");
        }
    }
}
