using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentsAndCovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cover",
                table: "Cards");

            migrationBuilder.AddColumn<string>(
                name: "CoverColor",
                table: "Cards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CoverAttachmentId",
                table: "Cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "varchar(256)", nullable: false),
                    Url = table.Column<string>(type: "varchar(2048)", nullable: true),
                    FileName = table.Column<string>(type: "varchar(256)", nullable: true),
                    MimeType = table.Column<string>(type: "varchar(255)", nullable: true),
                    Bytes = table.Column<int>(type: "integer", nullable: true),
                    ObjectKey = table.Column<string>(type: "varchar(512)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.CheckConstraint("CK_Attachments_Kind", "(\"Kind\" = 'File'\n    AND \"Url\" IS NULL\n    AND \"FileName\" IS NOT NULL AND \"MimeType\" IS NOT NULL\n    AND \"Bytes\" IS NOT NULL AND \"ObjectKey\" IS NOT NULL)\nOR (\"Kind\" = 'Link'\n    AND \"Url\" IS NOT NULL\n    AND \"FileName\" IS NULL AND \"MimeType\" IS NULL\n    AND \"Bytes\" IS NULL AND \"ObjectKey\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Attachments_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Attachments_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attachments_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PendingObjectDeletes",
                columns: table => new
                {
                    ObjectKey = table.Column<string>(type: "varchar(512)", nullable: false),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingObjectDeletes", x => x.ObjectKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_CoverAttachmentId",
                table: "Cards",
                column: "CoverAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_CardId",
                table: "Attachments",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_CreatedBy",
                table: "Attachments",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UpdatedBy",
                table: "Attachments",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PendingObjectDeletes_NotBefore",
                table: "PendingObjectDeletes",
                column: "NotBefore");

            migrationBuilder.AddForeignKey(
                name: "FK_Cards_Attachments_CoverAttachmentId",
                table: "Cards",
                column: "CoverAttachmentId",
                principalTable: "Attachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(QueueObjectDeleteFunction);
            migrationBuilder.Sql(QueueObjectDeleteTrigger);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER \"Attachments_QueueObjectDelete\" ON \"Attachments\";");
            migrationBuilder.Sql("DROP FUNCTION \"QueueObjectDelete\"();");

            migrationBuilder.DropForeignKey(
                name: "FK_Cards_Attachments_CoverAttachmentId",
                table: "Cards");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "PendingObjectDeletes");

            migrationBuilder.DropIndex(
                name: "IX_Cards_CoverAttachmentId",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "CoverAttachmentId",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "CoverColor",
                table: "Cards");

            migrationBuilder.AddColumn<string>(
                name: "Cover",
                table: "Cards",
                type: "text",
                nullable: true);
        }

        private const string QueueObjectDeleteFunction = """
            CREATE FUNCTION "QueueObjectDelete"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD."ObjectKey" IS NOT NULL THEN
                    INSERT INTO "PendingObjectDeletes" ("ObjectKey", "NotBefore")
                    VALUES (OLD."ObjectKey", CURRENT_TIMESTAMP)
                    ON CONFLICT ("ObjectKey") DO NOTHING;
                END IF;
                RETURN NULL;
            END;
            $$;
            """;

        private const string QueueObjectDeleteTrigger = """
            CREATE TRIGGER "Attachments_QueueObjectDelete"
            AFTER DELETE ON "Attachments"
            FOR EACH ROW
            EXECUTE FUNCTION "QueueObjectDelete"();
            """;
    }
}
