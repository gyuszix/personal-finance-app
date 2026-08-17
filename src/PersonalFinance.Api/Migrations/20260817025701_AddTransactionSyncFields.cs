using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategorDetailed",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryPrimary",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPending",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PendingTransactionId",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncCursor",
                table: "Accounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategorDetailed",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategoryPrimary",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsPending",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PendingTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SyncCursor",
                table: "Accounts");
        }
    }
}
