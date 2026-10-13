using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BalanceSnapshots",
                columns: table => new
                {
                    BalanceSnapshotId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceSnapshots", x => x.BalanceSnapshotId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceSnapshots_AccountId_Date",
                table: "BalanceSnapshots",
                columns: new[] { "AccountId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BalanceSnapshots_UserId_Date",
                table: "BalanceSnapshots",
                columns: new[] { "UserId", "Date" });

            // Start the history from today's balances rather than waiting
            // for the next sync.
            migrationBuilder.Sql(
                """
                INSERT INTO "BalanceSnapshots" ("AccountId", "UserId", "Date", "Balance")
                SELECT "AccountId", "UserId", CURRENT_DATE, "Balance" FROM "Accounts";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BalanceSnapshots");
        }
    }
}
