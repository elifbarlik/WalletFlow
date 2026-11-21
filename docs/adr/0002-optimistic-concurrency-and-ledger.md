# ADR 0002 — Optimistic concurrency + double-entry ledger

**Status:** accepted

**Context.** Two simultaneous transfers must never lose an update or overdraw an account.

**Decision.** Every `Account` row carries PostgreSQL's `xmin` as a concurrency token. A transfer loads both accounts,
mutates them in the domain, and writes balances + `Transaction` + two `LedgerEntry` rows in **one** DB transaction.
If another writer changed a row, `SaveChanges` fails, the unit of work rolls back, clears the change tracker and retries
(up to 10 times, randomized backoff). A `CHECK (balance >= 0)` constraint is the last line of defence.

**Why not `SELECT … FOR UPDATE`?** Contention per account is low in a wallet; optimistic locking avoids holding row
locks across application code and cannot deadlock. For a hot account (e.g. a merchant) pessimistic locking would be the
next step.

**Verification.** Integration tests fire 20 concurrent transfers and assert exact balances, that an overdraw attempt
never goes negative, and that each account balance equals the sum of its signed ledger entries.
