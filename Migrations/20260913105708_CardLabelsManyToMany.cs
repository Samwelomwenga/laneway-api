using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class CardLabelsManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cards_Labels_LabelId",
                table: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_Cards_LabelId",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "LabelId",
                table: "Cards");

            migrationBuilder.CreateTable(
                name: "CardLabels",
                columns: table => new
                {
                    CardsId = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardLabels", x => new { x.CardsId, x.LabelsId });
                    table.ForeignKey(
                        name: "FK_CardLabels_Cards_CardsId",
                        column: x => x.CardsId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardLabels_Labels_LabelsId",
                        column: x => x.LabelsId,
                        principalTable: "Labels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardLabels_LabelsId",
                table: "CardLabels",
                column: "LabelsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardLabels");

            migrationBuilder.AddColumn<Guid>(
                name: "LabelId",
                table: "Cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_LabelId",
                table: "Cards",
                column: "LabelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cards_Labels_LabelId",
                table: "Cards",
                column: "LabelId",
                principalTable: "Labels",
                principalColumn: "Id");
        }
    }
}
