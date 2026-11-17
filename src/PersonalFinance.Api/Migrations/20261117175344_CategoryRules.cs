using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class CategoryRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryRuleId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaidCategoryPrimary",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoryRules",
                columns: table => new
                {
                    CategoryRuleId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MatchType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: true),
                    MinAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryRules", x => x.CategoryRuleId);
                    table.ForeignKey(
                        name: "FK_CategoryRules_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryRuleId",
                table: "Transactions",
                column: "CategoryRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_AccountId",
                table: "CategoryRules",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_UserId_Priority",
                table: "CategoryRules",
                columns: new[] { "UserId", "Priority" });

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_CategoryRules_CategoryRuleId",
                table: "Transactions",
                column: "CategoryRuleId",
                principalTable: "CategoryRules",
                principalColumn: "CategoryRuleId",
                onDelete: ReferentialAction.SetNull);

            // Until now CategoryPrimary *was* Plaid's category (#124).
            migrationBuilder.Sql("""UPDATE "Transactions" SET "PlaidCategoryPrimary" = "CategoryPrimary";""");

            // Same per-user row-level security as the other user tables (#119).
            // pf_app's grants come from the default privileges set there.
            migrationBuilder.Sql("""
                ALTER TABLE "CategoryRules" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "CategoryRules" FORCE ROW LEVEL SECURITY;
                CREATE POLICY user_isolation ON "CategoryRules"
                    USING ("UserId" = current_setting('app.user_id', true) OR current_setting('app.rls_bypass', true) = 'on')
                    WITH CHECK ("UserId" = current_setting('app.user_id', true) OR current_setting('app.rls_bypass', true) = 'on');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to Plaid's own categories before the rules go.
            migrationBuilder.Sql("""UPDATE "Transactions" SET "CategoryPrimary" = "PlaidCategoryPrimary";""");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_CategoryRules_CategoryRuleId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "CategoryRules");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CategoryRuleId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategoryRuleId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PlaidCategoryPrimary",
                table: "Transactions");
        }
    }
}
