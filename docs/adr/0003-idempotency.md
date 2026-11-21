# ADR 0003 — Idempotency: two independent layers

**Status:** accepted (supersedes the first version, which had a documented crash window)

**Context.** Clients retry after timeouts. A retried transfer or top-up must not move money twice, even if the
process crashes mid-request or the key store loses its data.

**Decision.** Two layers with different jobs:

1. **Key layer (fast, replays the response).** Money-moving commands require an `Idempotency-Key`. A pipeline behavior
   fingerprints the request (SHA-256 of the body), reserves `(user, key)` atomically, runs the handler once and stores the
   serialized response. A retry replays it; a different body under the same key → `422`; a concurrent duplicate → `409`.
   The store is pluggable: **Redis** (`SET NX EX`, default) or **PostgreSQL** (unique index).
2. **Database layer (authoritative, cannot be lost).** The transaction id is *derived* from `(user, operation, key)`
   (`IdempotentIds`). Because it is the primary key, the database rejects a second execution atomically with the money
   movement. If layer 1 has forgotten the request (Redis flush, failover, expiry), the handler finds the existing
   transaction and returns it; a different amount/accounts under the same key still gives `422`.
   The same id is sent to the payment provider as *its* idempotency key, so a top-up retried after a timeout can never
   charge twice.

**Failure semantics.**
- Redis down → money endpoints fail **closed** (`503`), because proceeding without layer 1 would be unsafe for the user experience (duplicates are still impossible thanks to layer 2).
- A top-up that failed because the provider was unreachable is reopened and retried with the same key; a *declined* top-up replays the decline (use a new key to try another card).

**Verification.** Unit tests (handlers replay without any store), integration tests on PostgreSQL and on Redis,
including "wipe every Redis key, replay the same request, balance unchanged", and 20 simultaneous requests with one key.
