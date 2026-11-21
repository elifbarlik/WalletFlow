# ADR 0001 — Clean Architecture with CQRS (MediatR)

**Status:** accepted

**Context.** The service has real business rules (balances, ledger, idempotency) that must be testable without a
database or web server, and cross-cutting concerns (validation, logging, idempotency) that should not leak into handlers.

**Decision.** Four layers with inward-only dependencies: `Domain` (no dependencies) ← `Application` (use cases,
interfaces) ← `Infrastructure` / `Api`. Each use case is one MediatR request + handler. Cross-cutting concerns are
pipeline behaviors: logging → validation → idempotency.

**Consequences.** + Domain and handlers are unit-testable with in-memory fakes. + Controllers stay 3–5 lines.
+ `NetArchTest` enforces the dependency rule in CI. - More files than a single-project CRUD app; accepted because the
rules the project demonstrates need the seams.
