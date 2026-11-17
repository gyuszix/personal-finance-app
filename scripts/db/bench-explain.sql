-- The hot queries exactly as EF Core generates them (ToQueryString), for
-- one user, under EXPLAIN (ANALYZE, BUFFERS). See docs/DB_INDEXES.md (#113).
\set ON_ERROR_STOP on
\set uid '''user-1234'''
\pset pager off

\echo '=== Q1 transaction list: newest first, page 1 ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON)
SELECT t."TransactionId", t."AccountId", t."Amount", t."CategoryDetailed", t."CategoryPrimary", t."Date", t."Description", t."IsPending", t."PendingTransactionId", t."PlaidTransactionId", t."UserId"
FROM "Transactions" AS t
WHERE t."UserId" = :uid
ORDER BY t."Date" DESC
LIMIT 50 OFFSET 0;

\echo '=== Q2 transaction list filtered by category ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON)
SELECT t."TransactionId", t."AccountId", t."Amount", t."CategoryDetailed", t."CategoryPrimary", t."Date", t."Description", t."IsPending", t."PendingTransactionId", t."PlaidTransactionId", t."UserId"
FROM "Transactions" AS t
WHERE t."UserId" = :uid AND t."CategoryPrimary" = 'FOOD_AND_DRINK'
ORDER BY t."Date" DESC
LIMIT 50 OFFSET 0;

\echo '=== Q3 monthly cash flow over 12 months (every trend insight) ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON)
SELECT t0."Year", t0."Month", COALESCE(sum(t0."Amount"), 0.0)
FROM (
    SELECT t."Amount", date_part('year', t."Date" AT TIME ZONE 'UTC')::int AS "Year", date_part('month', t."Date" AT TIME ZONE 'UTC')::int AS "Month"
    FROM "Transactions" AS t
    WHERE t."UserId" = :uid AND NOT (t."IsPending") AND (t."CategoryPrimary" <> 'TRANSFER_IN' OR t."CategoryPrimary" IS NULL) AND (t."CategoryPrimary" <> 'TRANSFER_OUT' OR t."CategoryPrimary" IS NULL) AND (t."CategoryDetailed" IS NULL OR t."CategoryDetailed" <> 'LOAN_PAYMENTS_CREDIT_CARD_PAYMENT')
      AND t."Date" >= timestamptz '2025-12-01' AND t."Date" < timestamptz '2026-12-01'
) AS t0
GROUP BY t0."Year", t0."Month";

\echo '=== Q4 credit vs debit spending: transactions joined to accounts, one month ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON)
SELECT a0."AccountType", t."Amount"
FROM "Transactions" AS t
INNER JOIN (
    SELECT a."AccountId", a."AccountType" FROM "Accounts" AS a WHERE a."UserId" = :uid
) AS a0 ON t."AccountId" = a0."AccountId"
WHERE t."UserId" = :uid AND NOT (t."IsPending") AND (t."CategoryPrimary" <> 'TRANSFER_IN' OR t."CategoryPrimary" IS NULL) AND (t."CategoryPrimary" <> 'TRANSFER_OUT' OR t."CategoryPrimary" IS NULL) AND (t."CategoryDetailed" IS NULL OR t."CategoryDetailed" <> 'LOAN_PAYMENTS_CREDIT_CARD_PAYMENT') AND t."Amount" > 0.0
  AND t."Date" >= timestamptz '2026-11-01' AND t."Date" < timestamptz '2026-12-01';

\echo '=== Q5 distinct categories (filter dropdown) ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON)
SELECT DISTINCT t."CategoryPrimary" FROM "Transactions" AS t
WHERE t."UserId" = :uid AND t."CategoryPrimary" IS NOT NULL;

\echo '=== Q6 accounts summary ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON)
SELECT a.* FROM "Accounts" AS a WHERE a."UserId" = :uid;

\echo '=== Q7 unlink: delete an Item''s transactions by account ==='
BEGIN;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON)
DELETE FROM "Transactions" WHERE "AccountId" IN (SELECT "AccountId" FROM "Accounts" WHERE "UserId" = :uid);
ROLLBACK;
