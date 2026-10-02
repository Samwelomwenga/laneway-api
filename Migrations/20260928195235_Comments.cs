using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class Comments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "ActivityEntries",
                type: "varchar(10000)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_CardId_Comment",
                table: "ActivityEntries",
                column: "CardId",
                filter: "\"Type\" = 'Comment'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityEntries_CardId_Comment",
                table: "ActivityEntries");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "ActivityEntries",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(10000)",
                oldNullable: true);
        }
    }
}
