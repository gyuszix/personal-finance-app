#!/usr/bin/env bash
# Reproduces docs/DB_INDEXES.md: starts a throwaway Postgres 16, applies the
# EF migrations (optionally only up to a given one), seeds 1M transactions
# and prints EXPLAIN (ANALYZE, BUFFERS) for the hot queries (as pf_app,
# under row-level security, with BENCH_AS_APP=1). Removes the
# container afterwards. Usage: scripts/db/bench.sh [TargetMigration]
set -euo pipefail
cd "$(dirname "$0")/../.."

NAME=pf-index-bench
PORT=55432
docker rm -f "$NAME" >/dev/null 2>&1 || true
docker run -d --name "$NAME" -e POSTGRES_HOST_AUTH_METHOD=trust -p "$PORT:5432" postgres:16-alpine >/dev/null
trap 'docker rm -f "$NAME" >/dev/null' EXIT
until docker exec "$NAME" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
sleep 1

dotnet ef database update ${1:-} --project src/PersonalFinance.Api \
  --connection "Host=localhost;Port=$PORT;Database=postgres;Username=postgres" >/dev/null

docker exec -i "$NAME" psql -q -U postgres < scripts/db/bench-seed.sql
docker exec -i "$NAME" psql -U postgres -c 'SELECT count(*) AS transactions FROM "Transactions"'
# Try a candidate index without writing a migration, e.g.
#   BENCH_EXTRA_SQL='CREATE INDEX ON "Transactions" ("UserId", "CategoryPrimary", "Date"); ANALYZE;'
if [ -n "${BENCH_EXTRA_SQL:-}" ]; then
  docker exec -i "$NAME" psql -q -U postgres -c "$BENCH_EXTRA_SQL"
fi
docker exec -i "$NAME" psql -U postgres -c "SELECT indexrelname AS index, pg_size_pretty(pg_relation_size(indexrelid)) AS size FROM pg_stat_user_indexes WHERE relname = 'Transactions' ORDER BY 1" 2>/dev/null | sed 's/^/  /' || true
if [ -n "${BENCH_AS_APP:-}" ]; then
  # As the API's role, with row-level security applying (#119): the session
  # settings RlsSessionInterceptor would set for user-1234.
  docker exec -i "$NAME" psql -q -U postgres -c "ALTER ROLE pf_app LOGIN"
  { echo "SELECT set_config('app.user_id', 'user-1234', false), set_config('app.rls_bypass', 'off', false);";
    cat scripts/db/bench-explain.sql; } | docker exec -i "$NAME" psql -U pf_app -d postgres
else
  docker exec -i "$NAME" psql -U postgres < scripts/db/bench-explain.sql
fi
