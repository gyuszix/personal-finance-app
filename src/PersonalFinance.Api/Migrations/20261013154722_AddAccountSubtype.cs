using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSubtype : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subtype",
                table: "Accounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Subtype",
                table: "Accounts");
        }
    }
}
