# ADR-0003: Modular monolith with folder-per-module

Date: 2026-10-10

## Context

F1 needs Documents, Parties and (later) Peppol. Microservices would force
distributed transactions and three deployables before the domain is stable.

## Decision

I keep a single deployable with modules as folders (Modules/Documents,
Modules/Parties) and a strict layering: Domain (pure) → Infrastructure → Api.
Module boundaries are enforced by endpoint groups and separate DTOs; shared
state only through the DbContext. Peppol will land as a new module folder.

## Alternatives considered

- One project per module assembly: more ceremony now, little isolation gain
  at this size; revisit when the team/repo grows.
- Microservices now: network boundaries without the operational maturity to
  support them (tracing, sagas) would hurt the demo.

## Consequences

- Extracting a module into a service later means moving a folder and keeping
  the outbox contract — the seam already exists via events.
