# ADR-0002: Transactional outbox + CloudEvents on RabbitMQ

Date: 2026-10-10

## Context

Creating an invoice must be visible to the PDF worker without dual-write bugs
between Postgres and RabbitMQ.

## Decision

I write the invoice and an OutboxMessage row in the same Postgres transaction.
A BackgroundService (OutboxDispatcher) polls unpublished rows and publishes
them as CloudEvents 1.0 structured-mode JSON to the topic exchange
`einvoice.events`, then marks them published. Delivery is at-least-once: the
consumer must deduplicate by invoiceId (implemented in the worker repo).

## Alternatives considered

- Direct publish after commit: a crash between commit and publish loses the
  event silently; a publish before commit emits events for rolled-back data.
- Debezium CDC: operationally heavier than this demo needs, and hides the
  pattern I want to explain in interviews.

## Consequences

- Duplicate events are possible; the worker's idempotency by invoiceId is part
  of the contract (see einvoice-worker).
- The dispatcher polls every Outbox:IntervalMs (default 2s); a future
  improvement is LISTEN/NOTIFY to remove polling latency.
