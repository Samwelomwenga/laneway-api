using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class FieldShapes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "ReminderDate",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Boards");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "Cards",
                newName: "IsDueComplete");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "WorkSpaces",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Lists",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Labels",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DueDate",
                table: "Cards",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "DueReminderMinutes",
                table: "Cards",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Boards",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueReminderMinutes",
                table: "Cards");

            migrationBuilder.RenameColumn(
                name: "IsDueComplete",
                table: "Cards",
                newName: "Status");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "WorkSpaces",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Lists",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Labels",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "DueDate",
                table: "Cards",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                table: "Cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderDate",
                table: "Cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Boards",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Boards",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Boards",
                type: "text",
                nullable: true);
        }
    }
}
