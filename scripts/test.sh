#!/usr/bin/env bash
# Runs the same checks as .github/workflows/ci.yml, locally. Integration tests
# start a throwaway Postgres 16 container via Testcontainers, so Docker must
# be running. Usage: scripts/test.sh [extra dotnet test args, e.g. --filter X]
set -euo pipefail

cd "$(dirname "$0")/.."
TESTS=src/PersonalFinance.Tests/PersonalFinance.Tests.csproj

if ! docker info >/dev/null 2>&1; then
  echo "Docker isn't running - the integration tests need it (Testcontainers)." >&2
  exit 1
fi

echo "==> Restore"
dotnet restore "$TESTS"

echo "==> Vulnerable packages"
dotnet list "$TESTS" package --vulnerable --include-transitive | tee /tmp/pf-vulnerable.txt
if grep -q "has the following vulnerable packages" /tmp/pf-vulnerable.txt; then
  echo "Vulnerable NuGet packages found - see above" >&2
  exit 1
fi

echo "==> Build"
dotnet build "$TESTS" --no-restore --configuration Release

echo "==> Test"
dotnet test "$TESTS" --no-build --configuration Release "$@"
