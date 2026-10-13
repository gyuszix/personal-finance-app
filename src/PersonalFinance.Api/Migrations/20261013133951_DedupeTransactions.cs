using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class DedupeTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before #41 each account in an Item ran its own Item-level
            // /transactions/sync, storing every transaction once per account
            // and mostly on the wrong one (#53). Keep one row per Plaid ID,
            // then clear the cursors so the next sync replays history -
            // SyncItemAsync's Added path re-attributes each kept row to the
            // account Plaid says it belongs to.
            migrationBuilder.Sql(
                """
                DELETE FROM "Transactions" t
                USING (
                    SELECT "TransactionId",
                           ROW_NUMBER() OVER (PARTITION BY "PlaidTransactionId" ORDER BY "TransactionId") AS rn
                    FROM "Transactions"
                ) ranked
                WHERE t."TransactionId" = ranked."TransactionId" AND ranked.rn > 1;

                UPDATE "Accounts" SET "SyncCursor" = NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PlaidTransactionId",
                table: "Transactions",
                column: "PlaidTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_PlaidTransactionId",
                table: "Transactions");
        }
    }
}
