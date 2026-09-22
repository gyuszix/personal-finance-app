# MCP servers for local agent tooling

Two [MCP](https://modelcontextprotocol.io) servers are configured at project
scope in [`.mcp.json`](../.mcp.json), so an agent session can introspect the
real database and work with repo issues/PRs through structured tools instead
of hand-rolled `psql` and `curl` calls.

An MCP server is a separate process (or a remote HTTP endpoint) that exposes
tools to the agent - it is not a dependency of the app, and nothing in
`src/` references it.

| Server | Transport | What it gives you |
|---|---|---|
| `postgres` | stdio, local process | Schema introspection, `EXPLAIN` plans, index/health analysis, read-only SQL |
| `github` | HTTP, GitHub-hosted | Issues, PRs, repos, code search |

Because `.mcp.json` is committed, Claude Code will ask you to approve both
servers the first time you open the project - running a command defined by a
repo is a trust decision, so that prompt is expected.

## One-time setup

### 1. The read-only database role

The Postgres server runs with `--access-mode restricted`, but that is
app-level enforcement only, and some Postgres MCP implementations have had
injection issues that bypass it. So access is *also* constrained by a
dedicated role with `SELECT`-only grants, which Postgres itself enforces.

Against the Postgres the API actually uses (see Notes - on this machine
that's the native Homebrew instance, not the container):

```bash
psql -h localhost -p 5432 -d personalfinance
```

```sql
CREATE ROLE claude_ro LOGIN;
GRANT CONNECT ON DATABASE personalfinance TO claude_ro;
GRANT USAGE ON SCHEMA public TO claude_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO claude_ro;
ALTER DEFAULT PRIVILEGES FOR ROLE gyuszix IN SCHEMA public
  GRANT SELECT ON TABLES TO claude_ro;
GRANT pg_monitor TO claude_ro;
```

- No password because Homebrew's `pg_hba.conf` uses `trust` for `127.0.0.1`.
  If that ever changes, put the connection string in an env var and
  reference it from `.mcp.json` as `${DATABASE_URI}` rather than inline.
- `ALTER DEFAULT PRIVILEGES` is the easy one to forget: without it, tables
  created by future `dotnet ef database update` runs are invisible to
  `claude_ro` until someone re-grants. `FOR ROLE gyuszix` because migrations
  run as that superuser, which owns the tables.
- `pg_monitor` is optional and only unlocks the vacuum/connection/cache
  health checks. It is read-only statistics access.

### 2. Install the Postgres server

```bash
brew install pipx && pipx ensurepath
pipx install postgres-mcp --python /opt/homebrew/bin/python3.13
pipx inject --force postgres-mcp "mcp<2"
```

Both pins are load-bearing, and neither is obvious from the error you get:

- **Python 3.13, not 3.14.** The `pglast` dependency has no wheel for 3.14
  and fails to compile against the current macOS SDK, where `strchrnul` is
  already declared (`static declaration ... follows non-static declaration`).
- **`mcp<2`.** `postgres-mcp` 0.3.0 predates the Python MCP SDK 2.x rename of
  `FastMCP` to `MCPServer`, so a fresh install resolves to an SDK it cannot
  import (`No module named 'mcp.server.fastmcp'`).

Confirm the entry point resolves, and note the path:

```bash
~/.local/bin/postgres-mcp --help
```

### 3. Authenticate GitHub

No token to create - the GitHub entry is just a URL and auth happens over
OAuth. Restart Claude Code so it picks up the new servers, then run `/mcp`,
select `github`, and complete the browser flow. An unauthenticated request
to the endpoint returning HTTP 401 is the healthy response.

## Verifying it works

`/mcp` should list both as connected. Beyond that, ask for something only a
working server can answer - the schema of the `Transactions` table, or the
open issues on this repo. Tools appear namespaced as
`mcp__postgres__*` and `mcp__github__*`, and go through the same permission
prompts as `Bash`.

To confirm read-only enforcement is real rather than assumed, ask for a
`DELETE`. It should be refused by the server's query validator, and the role
would refuse it too:

```bash
psql "postgresql://claude_ro@localhost:5432/personalfinance" \
  -c 'delete from "Transactions"'
# ERROR:  permission denied for table Transactions
```

## Notes

- **Which Postgres this points at.** The connection string targets whatever
  holds `localhost:5432`. On this machine that is the native Homebrew
  `postgresql@16` service running as `gyuszix` - *not* the `docker-compose`
  container, which is stopped and whose port mapping would collide with it.
  The MCP server has to match wherever `ConnectionStrings:Default` actually
  resolves, or it will introspect an empty database and quietly mislead you.
  This is the same trap as #42, and the README's Local development section
  still describes only the container path.
- **The `command` path is machine-specific.** `pipx` installs to
  `~/.local/bin`, which is not on the PATH Claude Code uses to spawn stdio
  servers, so `.mcp.json` hardcodes an absolute path under `/Users/gyuszix`.
  Anyone else cloning this will need to change it - if that ever happens,
  move it to an env var placeholder.
- **The remote GitHub server has no read-only mode.** That is a flag on the
  self-hosted version only. It can write - create issues, push, merge - so
  the permission prompts are the real guardrail. It enables 5 of its 22
  toolsets by default (context, repos, issues, pull_requests, users), which
  is why no toolset filtering is configured here; each tool definition costs
  context on every request, so this is worth revisiting if more are enabled.
- **Index tuning is not fully wired up.** `analyze_workload_indexes` and
  `get_top_queries` want the `pg_stat_statements` and `hypopg` extensions.
  `pg_stat_statements` needs a `shared_preload_libraries` change and a
  restart; `hypopg` needs installing separately. Schema introspection,
  `EXPLAIN`, and read-only SQL all work without them. Not worth doing until
  there is an actual slow query to chase.
