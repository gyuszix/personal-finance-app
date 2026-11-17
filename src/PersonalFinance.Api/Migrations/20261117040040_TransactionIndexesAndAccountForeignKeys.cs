using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class TransactionIndexesAndAccountForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows whose account is already gone can't satisfy the new foreign
            // keys. They're unreachable leftovers (an account can only be
            // removed by unlinking, which deletes its rows), but they would
            // still count in the owner's totals through the UserId filter.
            migrationBuilder.Sql("""
                DELETE FROM "Transactions" t
                WHERE NOT EXISTS (SELECT 1 FROM "Accounts" a WHERE a."AccountId" = t."AccountId");
                DELETE FROM "BalanceSnapshots" s
                WHERE NOT EXISTS (SELECT 1 FROM "Accounts" a WHERE a."AccountId" = s."AccountId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_Date",
                table: "Transactions",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BalanceSnapshots_Accounts_AccountId",
                table: "BalanceSnapshots",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "AccountId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "AccountId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BalanceSnapshots_Accounts_AccountId",
                table: "BalanceSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_Date",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts");
        }
    }
}
