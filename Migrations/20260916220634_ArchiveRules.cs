using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boards_WorkSpaces_WorkspaceId",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "WorkSpaces");

            migrationBuilder.AddForeignKey(
                name: "FK_Boards_WorkSpaces_WorkspaceId",
                table: "Boards",
                column: "WorkspaceId",
                principalTable: "WorkSpaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boards_WorkSpaces_WorkspaceId",
                table: "Boards");

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "WorkSpaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_Boards_WorkSpaces_WorkspaceId",
                table: "Boards",
                column: "WorkspaceId",
                principalTable: "WorkSpaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
