using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountPlaidItemId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlaidItemId",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_PlaidItemId",
                table: "Accounts",
                column: "PlaidItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_PlaidItemId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PlaidItemId",
                table: "Accounts");
        }
    }
}
