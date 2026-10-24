using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laneway.Api.Migrations
{
    /// <inheritdoc />
    public partial class LowercaseUsernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           lower(btrim("Username")) AS "Name",
                           row_number() OVER (
                               PARTITION BY lower(btrim("Username")), "DeletedAt" IS NULL
                               ORDER BY "CreatedAt", "Id") AS "Place"
                    FROM "Users")
                UPDATE "Users" AS u
                SET "Username" = 'user-' || left(replace(u."Id"::text, '-', ''), 8)
                FROM ranked
                WHERE u."Id" = ranked."Id"
                  AND (ranked."Place" > 1 OR ranked."Name" !~ '^[a-z0-9][a-z0-9_-]{2,49}$');
                """);
            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "Username" = lower(btrim("Username"))
                WHERE "Username" <> lower(btrim("Username"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
