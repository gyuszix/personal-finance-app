-- Seeds a benchmark volume into an EMPTY, migrated database - never run
-- this against a database with real data. Used by scripts/db/bench.sh to
-- reproduce the plans in docs/DB_INDEXES.md (#113).
--
-- 2,000 users x 5 accounts x 100 transactions = 1,000,000 transactions over
-- three years (about 500 per user: a few months of real card activity), plus a
-- year of daily balance snapshots per account.
\set ON_ERROR_STOP on
SELECT setseed(0.42);

INSERT INTO "Accounts" ("UserId", "Balance", "BankName", "AccountType", "Subtype", "PlaidAccountId", "PlaidAccessToken", "SyncCursor")
SELECT 'user-' || u, round((random() * 20000)::numeric, 2), 'Bench Bank',
       (ARRAY['Depository','Depository','Credit','Loan','Investment'])[a],
       NULL, 'acct-' || u || '-' || a, 'item-' || u, NULL
FROM generate_series(1, 2000) u, generate_series(1, 5) a;

INSERT INTO "Transactions" ("AccountId", "Amount", "Description", "Date", "PlaidTransactionId", "UserId",
                            "IsPending", "PendingTransactionId", "CategoryPrimary", "CategoryDetailed")
SELECT acc."AccountId",
       round((random() * 300 - 40)::numeric, 2),
       'Merchant ' || (random() * 200)::int,
       timestamptz '2026-11-16' - (random() * interval '1095 days'),
       'txn-' || acc."AccountId" || '-' || n,
       acc."UserId",
       random() < 0.02, NULL,
       (ARRAY['FOOD_AND_DRINK','GENERAL_MERCHANDISE','TRANSPORTATION','RENT_AND_UTILITIES','ENTERTAINMENT',
              'INCOME','TRANSFER_IN','TRANSFER_OUT','LOAN_PAYMENTS','PERSONAL_CARE'])[1 + (random() * 9)::int],
       NULL
FROM "Accounts" acc, generate_series(1, 100) n;

INSERT INTO "BalanceSnapshots" ("AccountId", "UserId", "Date", "Balance")
SELECT acc."AccountId", acc."UserId", d::date, round((random() * 20000)::numeric, 2)
FROM "Accounts" acc, generate_series(date '2025-11-17', date '2026-11-16', interval '1 day') d;

VACUUM ANALYZE;
