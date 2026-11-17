using Microsoft.EntityFrameworkCore;

namespace PersonalFinance.Api.Data;

// Row-level security does nothing for a superuser or a BYPASSRLS role, and
// a table's owner can switch it off, so the API must connect as a plain
// role such as pf_app (#119). Checked at startup: a warning in development,
// a refusal to start in Production.
public static class DatabaseRoleCheck
{
    public record Posture(string Role, bool IsSuperuser, bool BypassesRls, bool OwnsTables)
    {
        public bool IsSafe => !IsSuperuser && !BypassesRls && !OwnsTables;
    }

    public static async Task<Posture> GetAsync(AppDbContext db)
    {
        var row = await db.Database.SqlQueryRaw<Posture>("""
            SELECT current_user::text AS "Role",
                   r.rolsuper AS "IsSuperuser",
                   r.rolbypassrls AS "BypassesRls",
                   EXISTS (SELECT 1 FROM pg_tables t
                           WHERE t.schemaname = 'public' AND t.tablename = 'Accounts'
                             AND pg_has_role(current_user, t.tableowner, 'USAGE')) AS "OwnsTables"
            FROM pg_roles r WHERE r.rolname = current_user
            """).SingleAsync();
        return row;
    }

    public static async Task EnforceAsync(AppDbContext db, IHostEnvironment env, ILogger logger)
    {
        var posture = await GetAsync(db);
        if (posture.IsSafe)
        {
            logger.LogInformation("Database role {Role} is subject to row-level security", posture.Role);
            return;
        }

        const string message =
            "The API is connected to Postgres as {Role} (superuser: {IsSuperuser}, BYPASSRLS: {BypassesRls}, " +
            "owns the tables: {OwnsTables}). Row-level security doesn't apply to it, so per-user isolation rests " +
            "on the EF query filters alone. Connect as pf_app instead - see README, 'Database roles'.";
        if (env.IsProduction())
        {
            logger.LogCritical(message, posture.Role, posture.IsSuperuser, posture.BypassesRls, posture.OwnsTables);
            throw new InvalidOperationException($"Refusing to start: database role {posture.Role} bypasses row-level security.");
        }
        logger.LogWarning(message, posture.Role, posture.IsSuperuser, posture.BypassesRls, posture.OwnsTables);
    }
}
