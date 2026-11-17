using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <summary>
    /// Postgres row-level security behind the EF query filters (#119), and
    /// the pf_app role the API runs as so that it actually applies.
    /// </summary>
    public partial class RowLevelSecurity : Migration
    {
        // Tables holding one user's data, by their "UserId" column.
        private static readonly string[] UserTables = ["Accounts", "Transactions", "BalanceSnapshots", "Budgets"];

        // A row is visible when it belongs to the user the API stamped on the
        // connection (RlsSessionInterceptor), or the code is explicitly in
        // DbSystemScope. Unset settings are NULL, which matches nothing, so
        // anything that forgets to say who it is fails closed.
        private const string Visible =
            """("UserId" = current_setting('app.user_id', true) OR current_setting('app.rls_bypass', true) = 'on')""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The API's role. Created without LOGIN here; whoever operates the
            // database gives it a password (README, "Database roles"). Roles
            // are cluster-wide, so tolerate another database having made it.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pf_app') THEN
                        CREATE ROLE pf_app NOLOGIN;
                    END IF;
                EXCEPTION WHEN duplicate_object THEN NULL;
                END $$;

                GRANT USAGE ON SCHEMA public TO pf_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO pf_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO pf_app;
                -- Tables later migrations create get the same.
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO pf_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO pf_app;

                -- The app reads its migration history but never writes it.
                REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON "__EFMigrationsHistory" FROM pf_app;
                -- Audit events: append and read only, beneath the trigger (#116).
                REVOKE UPDATE, DELETE, TRUNCATE ON "AuditEvents" FROM pf_app;
                """);

            foreach (var table in UserTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    -- FORCE: applies to the table owner too, not just other roles.
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY user_isolation ON "{table}" USING {Visible} WITH CHECK {Visible};
                    """);
            }

            // Audit events: anyone may append (failed logins and rejected
            // webhooks have no user), but only the user - or system code -
            // can read a user's events.
            migrationBuilder.Sql($"""
                ALTER TABLE "AuditEvents" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "AuditEvents" FORCE ROW LEVEL SECURITY;
                CREATE POLICY audit_append ON "AuditEvents" FOR INSERT WITH CHECK (true);
                CREATE POLICY audit_read_own ON "AuditEvents" FOR SELECT USING {Visible};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS audit_read_own ON "AuditEvents";
                DROP POLICY IF EXISTS audit_append ON "AuditEvents";
                ALTER TABLE "AuditEvents" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "AuditEvents" DISABLE ROW LEVEL SECURITY;
                """);
            foreach (var table in UserTables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS user_isolation ON "{table}";
                    ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }
            // pf_app and its grants are left in place: other databases on the
            // cluster may use the role, and dropping it would cut off the API.
        }
    }
}
