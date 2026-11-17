using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UserId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "UserId", "OccurredAt" });

            // Append-only at the database, not just by convention: an audit
            // trail the app could quietly edit isn't evidence. TRUNCATE is
            // covered too (#116). The table's owner can still drop the
            // trigger - so the API must not run as the owner (#119).
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_events_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'AuditEvents is append-only (% rejected)', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER audit_events_no_update_delete
                    BEFORE UPDATE OR DELETE ON "AuditEvents"
                    FOR EACH ROW EXECUTE FUNCTION audit_events_append_only();

                CREATE TRIGGER audit_events_no_truncate
                    BEFORE TRUNCATE ON "AuditEvents"
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_events_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_events_no_truncate ON "AuditEvents";
                DROP TRIGGER IF EXISTS audit_events_no_update_delete ON "AuditEvents";
                DROP FUNCTION IF EXISTS audit_events_append_only();
                """);

            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
