# ADR-0004: Peppol access point deferred from v1

Date: 2026-10-10

## Context

The family vision includes Peppol (AS4, business messages). Implementing it
now would delay the API+worker demo and pull in certificate/PEPPOL-directory
complexity that does not serve the first interviews.

## Decision

I ship v1 with REST invoices + parties + outbox events only. Peppol is on the
README roadmap as the next major capability of the family and will be added as
a new module following ADR-0003.

## Consequences

- No AS4/Peppol validation in v1; the event contract is designed to carry a
  future Peppol message id without breaking changes (versioned event types).
