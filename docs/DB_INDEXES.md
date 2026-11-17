# Database indexes

Why `Transactions` and `Accounts` have the indexes they have, with the
measured plans behind each one (#113). Re-run the measurements yourself with:

```bash
scripts/db/bench.sh                 # current schema
scripts/db/bench.sh MoneyPrecision  # schema as of an earlier migration (before these indexes)
BENCH_EXTRA_SQL='CREATE INDEX ...; ANALYZE;' scripts/db/bench.sh   # try a candidate index
```

`bench.sh` starts a throwaway Postgres 16 container, applies the EF
migrations, seeds [`bench-seed.sql`](../scripts/db/bench-seed.sql), runs
[`bench-explain.sql`](../scripts/db/bench-explain.sql) and removes the
container again. It never touches your dev database.

## Data volume

- 2,000 users, 5 accounts each, 100 transactions per account. That's
  **1,000,000 transactions** (~500 per user) over three years, and about
  3.6M balance snapshots (a year, daily, per account).
- Values are random but shaped like the real data: about 2% pending, a
  realistic spread of Plaid categories including transfers and income.
  Statistics are fresh (`VACUUM ANALYZE`).

The point isn't the absolute numbers. It's that every per-user query
otherwise reads the **whole table** to return one user's handful of rows,
and that cost grows with the number of users, not with the size of the
user's own data.

## The queries

All seven are the SQL EF Core actually generates (captured with
`ToQueryString()`). The `"UserId" = @user` predicate comes from the global
query filter in `AppDbContext`, so it's on every one of them.

| # | Endpoint / caller | Shape |
|---|---|---|
| Q1 | `GET /transactions` (page 1) | `WHERE UserId = @u ORDER BY Date DESC LIMIT 50` |
| Q2 | `GET /transactions?category=` | `... AND CategoryPrimary = @c ORDER BY Date DESC LIMIT 50` |
| Q3 | `MonthlyCashflow` (cash flow, savings rate, burn rate, budgets) | 12-month date range, `CountedInCashflow` predicates, `GROUP BY` year/month |
| Q4 | `PaymentMethod` (credit vs debit cards) | one month, `Transactions JOIN Accounts` |
| Q5 | `GET /transactions/categories` | `SELECT DISTINCT CategoryPrimary WHERE UserId = @u` |
| Q6 | `GET /accounts/summary` and every `Accounts` read | `WHERE UserId = @u` |
| Q7 | `DELETE /accounts/{id}` (unlink) | delete transactions `WHERE AccountId IN (...)` |

## Before → after

`EXPLAIN (ANALYZE, BUFFERS)`. A buffer is an 8 kB page, so buffers are a
better measure of the work done than milliseconds on a warm laptop.

| # | Before: plan | Buffers | Time | After: plan | Buffers | Time |
|---|---|---:|---:|---|---:|---:|
| Q1 | Parallel Seq Scan + top-N sort | 14,458 | 19.1 ms | Index Scan **Backward** on `(UserId, Date)`, stops after 50 rows | 53 | 0.24 ms |
| Q2 | Parallel Seq Scan + sort | 14,458 | 19.8 ms | Bitmap Index Scan on `(UserId, Date)` (user's ~500 rows), filter category, sort | 508 | 1.7 ms |
| Q3 | Parallel Seq Scan | 14,456 | 27.2 ms | Bitmap Index Scan on `(UserId, Date)`, date range in the index condition | 171 | 0.19 ms |
| Q4 | Parallel Seq Scan + PK lookups | 14,398 | 33.1 ms | Bitmap scans on `(UserId, Date)` and `Accounts(UserId)` | 18 | 0.08 ms |
| Q5 | Parallel Seq Scan + sort | 14,458 | 20.0 ms | Bitmap Index Scan on `(UserId, Date)` | 505 | 0.27 ms |
| Q6 | Seq Scan on `Accounts` | 126 | 0.78 ms | Bitmap Index Scan on `Accounts(UserId)` | 7 | 0.02 ms |
| Q7 | Seq Scan of all 1M transactions per unlink | 15,510 | 55.0 ms | Bitmap Index Scan on `Transactions(AccountId)` | 1,522 | 1.6 ms |

Q1 is the one that matters most. Before, the list read the whole table and
sorted the user's rows just to show the first page. After, it walks the index
backwards from the user's newest transaction and stops at row 50, so the cost
no longer depends on table size at all.

## What was added

Migration `TransactionIndexesAndAccountForeignKeys`:

| Index | Serves | Why this shape |
|---|---|---|
| `IX_Transactions_UserId_Date` on `(UserId, Date)` | Q1-Q5 and every insight | Equality column first, range/sort column second. One index answers "this user's rows", "in this date range" and "newest first" (Postgres scans a B-tree backwards for `DESC`, so no `DESC` index is needed) |
| `IX_Accounts_UserId` | Q6, Q4's join, every `Accounts` read | Every account read is per user |
| `IX_Transactions_AccountId` | Q7 | Comes with the new foreign key (below). Unlinking and FK cascade checks look up transactions by account |

The migration also adds **foreign keys** from `Transactions.AccountId` and
`BalanceSnapshots.AccountId` to `Accounts` (`ON DELETE CASCADE`). They were
missing entirely, so nothing stopped an orphaned transaction from staying
in a user's totals. It deletes any existing orphans first so the
constraint can be added. `BalanceSnapshots` needs no new index for its
FK: the existing unique `(AccountId, Date)` already starts with `AccountId`.

## Considered and rejected

**`(UserId, CategoryPrimary, Date)` for the category filter (Q2/Q5).**
Measured with `BENCH_EXTRA_SQL`:

| | `(UserId, Date)` only | + candidate |
|---|---:|---:|
| Q2 category list | 508 buffers, 1.7 ms | 35 buffers, 0.15 ms |
| Q5 distinct categories | 505 buffers, 0.27 ms | 8 buffers, 0.08 ms (Index Only Scan) |
| Index size | 47 MB | **+50 MB** |

It's faster, but the existing index already brings the category queries
down to the user's own ~500 rows, under 2 ms. The candidate costs more
disk than the main index and adds another index write to every transaction
a sync inserts. **Revisit** if a user's history reaches tens of thousands of
rows, or if category filtering shows up in `pg_stat_statements`.

**Indexes on `IsPending` / category columns alone.** These have low
selectivity (98% of rows aren't pending, ~10 categories). The planner
would rather filter the user's rows than use them.

**A partial index `WHERE NOT "IsPending"`.** Most insight queries do
filter on it, but it excludes only ~2% of rows, so the saving doesn't
justify a second copy of the main index.

## Under row-level security

Since #119 the API runs as `pf_app` with RLS policies on these tables
(`BENCH_AS_APP=1 scripts/db/bench.sh`). Every query still uses the same
indexes, because the EF filter's own `"UserId" = @user` stays indexable.
One plan changes shape: **Q1** goes from an Index Scan Backward that stops at
row 50 (0.24 ms) to a Bitmap Index Scan of the user's ~500 rows plus a
top-N sort (**1.97 ms**). The policy's
`"UserId" = current_setting(...) OR current_setting('app.rls_bypass') = 'on'`
has to be checked before the query's own conditions (it's a security
barrier), and the `OR` stops the planner using the index order to stop
early. The cost is bounded by the user's own row count, not the table
size, which is the property the index exists for. If it ever matters,
the fix is a policy without the `OR`: system jobs would run under a
separate `BYPASSRLS` role instead of the session flag, which needs a
superuser to create.

## Notes

- `UserId` is a 36-character text GUID (ASP.NET Identity's default), which
  is most of why `(UserId, Date)` is 47 MB per million rows. A native `uuid`
  column would roughly halve it, but that means changing Identity's key
  type. Not worth it at this size.
- Migrations run in a transaction, so `CREATE INDEX` takes a lock that
  blocks writes to `Transactions` while it builds. On a large live table, build the index with
  `CREATE INDEX CONCURRENTLY` outside a transaction first; the migration's
  `CREATE INDEX` then needs `IF NOT EXISTS`.
- `RefreshTokens` is looked up by token value with no index at all. That's
  fixed in #115, which replaces the column with a unique hash.
