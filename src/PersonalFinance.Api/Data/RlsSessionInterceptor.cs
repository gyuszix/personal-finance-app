using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PersonalFinance.Api.Data;

// Tells Postgres who's asking, every time EF opens a connection, so the
// row-level security policies can enforce per-user isolation on their own -
// a second layer behind AppDbContext's query filters (#119):
//   app.user_id    the signed-in user, or '' (matches no rows)
//   app.rls_bypass 'on' only inside a DbSystemScope outside any request
// Session-level (not transaction-local) because EF opens and closes the
// connection per command outside a transaction; pooled connections are
// re-stamped on every open, so a value never carries over to the next user.
public class RlsSessionInterceptor(IHttpContextAccessor httpContextAccessor) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('app.user_id', @user_id, false), set_config('app.rls_bypass', @bypass, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = Build(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = Build(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand Build(DbConnection connection)
    {
        var (userId, bypass) = Resolve();
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        Add(command, "user_id", userId ?? "");
        Add(command, "bypass", bypass ? "on" : "off");
        return command;
    }

    private (string? UserId, bool Bypass) Resolve()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
            return (httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), false);

        return (null, DbSystemScope.IsActive);
    }

    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
