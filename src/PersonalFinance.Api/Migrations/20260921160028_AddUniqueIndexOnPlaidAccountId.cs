using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexOnPlaidAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Earlier /plaid/exchange-token had no dedup, so re-linking the
            // same Item repeatedly left duplicate rows per PlaidAccountId.
            // Keep the newest row, move its transactions over, drop the rest.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "AccountId", "PlaidAccountId",
                           ROW_NUMBER() OVER (PARTITION BY "PlaidAccountId" ORDER BY "AccountId" DESC) AS rn
                    FROM "Accounts"
                ),
                keepers AS (
                    SELECT "PlaidAccountId", "AccountId" AS keeper_id FROM ranked WHERE rn = 1
                ),
                losers AS (
                    SELECT r."AccountId", k.keeper_id
                    FROM ranked r
                    JOIN keepers k ON k."PlaidAccountId" = r."PlaidAccountId"
                    WHERE r.rn > 1
                )
                UPDATE "Transactions" t
                SET "AccountId" = l.keeper_id
                FROM losers l
                WHERE t."AccountId" = l."AccountId";

                WITH ranked AS (
                    SELECT "AccountId",
                           ROW_NUMBER() OVER (PARTITION BY "PlaidAccountId" ORDER BY "AccountId" DESC) AS rn
                    FROM "Accounts"
                )
                DELETE FROM "Accounts" WHERE "AccountId" IN (SELECT "AccountId" FROM ranked WHERE rn > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_PlaidAccountId",
                table: "Accounts",
                column: "PlaidAccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_PlaidAccountId",
                table: "Accounts");
        }
    }
}
