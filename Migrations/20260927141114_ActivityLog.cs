using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class ActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: true),
                    ListId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromWorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromBoardId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromListId = table.Column<Guid>(type: "uuid", nullable: true),
                    Data = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityEntries_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_BoardId_CreatedAt_Id",
                table: "ActivityEntries",
                columns: new[] { "BoardId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_CardId_CreatedAt_Id",
                table: "ActivityEntries",
                columns: new[] { "CardId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_CreatedBy",
                table: "ActivityEntries",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_FromBoardId_CreatedAt_Id",
                table: "ActivityEntries",
                columns: new[] { "FromBoardId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_FromListId_CreatedAt_Id",
                table: "ActivityEntries",
                columns: new[] { "FromListId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_FromWorkspaceId_CreatedAt_Id",
                table: "ActivityEntries",
                columns: new[] { "FromWorkspaceId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_ListId_CreatedAt_Id",
                table: "ActivityEntries",
                columns: new[] { "ListId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_WorkspaceId_CreatedAt_Id",
                table: "ActivityEntries",
                columns: new[] { "WorkspaceId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEntries");
        }
    }
}
