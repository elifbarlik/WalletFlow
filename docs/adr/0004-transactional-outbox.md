# ADR 0004 — Transactional outbox + RabbitMQ

**Status:** accepted

**Problem.** After a transfer commits we must notify the customer. Publishing to a broker inside the request can lose
the event (crash after commit) or announce a transfer that rolled back.

**Decision.** Handlers write an `OutboxMessage` in the same DB transaction as the state change. A background
`OutboxDispatcher` polls with `FOR UPDATE SKIP LOCKED`, publishes through `IMessagePublisher`, and marks rows processed.
Delivery is **at-least-once**; the RabbitMQ publisher uses a durable topic exchange, persistent messages and publisher
confirms. `Notification.Worker` consumes idempotently (dedupe by message id) with a dead-letter queue.

**Configuration.** `Messaging__Provider=Log` (default, no broker) or `RabbitMq` (`--profile messaging`). The broker is behind `IMessagePublisher`, so another one (Kafka, a cloud bus) can be added without touching the handlers.

**Consumer.** The worker keeps an inbox in Redis (message id, 7 days), so redelivery never produces a second notification and several worker replicas can run. A failing message is dead-lettered (RabbitMQ DLQ) instead of looping.

**Limitations.** The dispatcher polls every 2 s (acceptable latency for notifications). Notifications are logged, not sent through a real e-mail/SMS provider.

