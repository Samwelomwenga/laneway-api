using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prodigy_board_API.Migrations
{
    /// <inheritdoc />
    public partial class LabelNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Labels_Name_Lower\" ON \"Labels\" (lower(\"Name\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_Labels_Name_Lower\";");
        }
    }
}
