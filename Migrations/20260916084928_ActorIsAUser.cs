using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class ActorIsAUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Accounts");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkSpaces_CreatedBy",
                table: "WorkSpaces",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSpaces_UpdatedBy",
                table: "WorkSpaces",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedBy",
                table: "Users",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UpdatedBy",
                table: "Users",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Lists_CreatedBy",
                table: "Lists",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Lists_UpdatedBy",
                table: "Lists",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Labels_CreatedBy",
                table: "Labels",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Labels_UpdatedBy",
                table: "Labels",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_CreatedBy",
                table: "Cards",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_UpdatedBy",
                table: "Cards",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_CreatedBy",
                table: "Boards",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_UpdatedBy",
                table: "Boards",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CreatedBy",
                table: "Accounts",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UpdatedBy",
                table: "Accounts",
                column: "UpdatedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Users_CreatedBy",
                table: "Accounts",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Users_UpdatedBy",
                table: "Accounts",
                column: "UpdatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Boards_Users_CreatedBy",
                table: "Boards",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Boards_Users_UpdatedBy",
                table: "Boards",
                column: "UpdatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cards_Users_CreatedBy",
                table: "Cards",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cards_Users_UpdatedBy",
                table: "Cards",
                column: "UpdatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Labels_Users_CreatedBy",
                table: "Labels",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Labels_Users_UpdatedBy",
                table: "Labels",
                column: "UpdatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lists_Users_CreatedBy",
                table: "Lists",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lists_Users_UpdatedBy",
                table: "Lists",
                column: "UpdatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_CreatedBy",
                table: "Users",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_UpdatedBy",
                table: "Users",
                column: "UpdatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkSpaces_Users_CreatedBy",
                table: "WorkSpaces",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkSpaces_Users_UpdatedBy",
                table: "WorkSpaces",
                column: "UpdatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Users_CreatedBy",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Users_UpdatedBy",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Boards_Users_CreatedBy",
                table: "Boards");

            migrationBuilder.DropForeignKey(
                name: "FK_Boards_Users_UpdatedBy",
                table: "Boards");

            migrationBuilder.DropForeignKey(
                name: "FK_Cards_Users_CreatedBy",
                table: "Cards");

            migrationBuilder.DropForeignKey(
                name: "FK_Cards_Users_UpdatedBy",
                table: "Cards");

            migrationBuilder.DropForeignKey(
                name: "FK_Labels_Users_CreatedBy",
                table: "Labels");

            migrationBuilder.DropForeignKey(
                name: "FK_Labels_Users_UpdatedBy",
                table: "Labels");

            migrationBuilder.DropForeignKey(
                name: "FK_Lists_Users_CreatedBy",
                table: "Lists");

            migrationBuilder.DropForeignKey(
                name: "FK_Lists_Users_UpdatedBy",
                table: "Lists");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_CreatedBy",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_UpdatedBy",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkSpaces_Users_CreatedBy",
                table: "WorkSpaces");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkSpaces_Users_UpdatedBy",
                table: "WorkSpaces");

            migrationBuilder.DropIndex(
                name: "IX_WorkSpaces_CreatedBy",
                table: "WorkSpaces");

            migrationBuilder.DropIndex(
                name: "IX_WorkSpaces_UpdatedBy",
                table: "WorkSpaces");

            migrationBuilder.DropIndex(
                name: "IX_Users_CreatedBy",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_UpdatedBy",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Lists_CreatedBy",
                table: "Lists");

            migrationBuilder.DropIndex(
                name: "IX_Lists_UpdatedBy",
                table: "Lists");

            migrationBuilder.DropIndex(
                name: "IX_Labels_CreatedBy",
                table: "Labels");

            migrationBuilder.DropIndex(
                name: "IX_Labels_UpdatedBy",
                table: "Labels");

            migrationBuilder.DropIndex(
                name: "IX_Cards_CreatedBy",
                table: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_Cards_UpdatedBy",
                table: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_Boards_CreatedBy",
                table: "Boards");

            migrationBuilder.DropIndex(
                name: "IX_Boards_UpdatedBy",
                table: "Boards");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_CreatedBy",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_UpdatedBy",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }
    }
}
