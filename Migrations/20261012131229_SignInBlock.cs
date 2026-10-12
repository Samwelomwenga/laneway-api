using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laneway.Api.Migrations
{
    /// <inheritdoc />
    public partial class SignInBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedSignInCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailedSignInAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignInBlockedUntil",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedSignInCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastFailedSignInAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SignInBlockedUntil",
                table: "Users");
        }
    }
}
