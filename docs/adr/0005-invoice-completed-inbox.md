# ADR-0005: Invoice completed inbox consumer

- Status: Accepted
- Date: 2026-10-12

## Context

Invoices were created as `Issued` and never updated: the worker published
`invoice.completed.v1` but no component consumed it, so the UI had no way to show
processing results. The domain already exposed `MarkProcessing`/`MarkCompleted`
with guard transitions.

## Decision

The API hosts an inbox consumer (`InvoiceCompletedConsumer`, BackgroundService)
on a durable queue `einvoice-api.invoice-completed` bound to the family exchange
`einvoice.events`. On `invoice.completed.v1` it loads the invoice and walks the
guarded transitions (`Issued → Processing → Completed`). Unknown invoices and
already-completed invoices are acknowledged (idempotent skip). Failures follow
the family retry pattern: `x-attempt` header, `min(15s×2ⁿ, 5min)` backoff via a
retry queue with per-message TTL, DLQ after 5 attempts or on poison payloads.

## Consequences

- Invoice status is now meaningful end-to-end; the UI renders it without polling
  Redis or the broker.
- The consumer mirrors the worker's retry topology (copy, not shared library) —
  acceptable duplication across repos for this study project; a shared package
  would be the evolution point in a real system.
- At-least-once delivery means the handler must stay idempotent (it is).
