using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class BoardScopedLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_Labels_Name_Lower\";");

            migrationBuilder.AddColumn<Guid>(
                name: "BoardId",
                table: "Labels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Labels_BoardId",
                table: "Labels",
                column: "BoardId");

            migrationBuilder.AddForeignKey(
                name: "FK_Labels_Boards_BoardId",
                table: "Labels",
                column: "BoardId",
                principalTable: "Boards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Labels_BoardId_Name_Lower\" ON \"Labels\" (\"BoardId\", lower(\"Name\")) WHERE \"Name\" <> '';");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Labels_BoardId_Color_Unnamed\" ON \"Labels\" (\"BoardId\", \"Color\") WHERE \"Name\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_Labels_BoardId_Color_Unnamed\";");
            migrationBuilder.Sql("DROP INDEX \"IX_Labels_BoardId_Name_Lower\";");

            migrationBuilder.DropForeignKey(
                name: "FK_Labels_Boards_BoardId",
                table: "Labels");

            migrationBuilder.DropIndex(
                name: "IX_Labels_BoardId",
                table: "Labels");

            migrationBuilder.DropColumn(
                name: "BoardId",
                table: "Labels");

            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Labels_Name_Lower\" ON \"Labels\" (lower(\"Name\"));");
        }
    }
}
