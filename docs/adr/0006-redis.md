# ADR 0006 — What Redis is (and is not) used for

**Status:** accepted

Redis is added only where an in-memory shared store solves a real problem, and never as the source of truth.

| Use | Why Redis | If Redis is lost / down |
|---|---|---|
| Idempotency keys (`SET NX EX`) | Sub-millisecond atomic reservation, TTL instead of cleanup jobs, no hot table in the ledger database | Layer 2 of [ADR 0003](0003-idempotency.md) (primary key) still prevents duplicates; money endpoints return `503` while it is down |
| Distributed rate limiting (Lua `INCR`+`PEXPIRE`) | ASP.NET's built-in limiter is per instance; with N replicas each would grant its own quota | **Fails open** (logged): protection must not take the API down |
| Notification inbox (`SET NX EX`, 7 days) | At-least-once delivery needs deduplication that survives restarts and works across worker replicas | A duplicate notification is possible, never a lost one |

Configuration: `ConnectionStrings__Redis`, `Idempotency__Provider=Redis|Postgres`. Redis runs with `noeviction` and
append-only persistence. Not used for caching
balances: a stale balance on a money API is worse than a slower read.
