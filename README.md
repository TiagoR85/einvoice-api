# einvoice-api

[![ci](https://github.com/TiagoR85/einvoice-api/actions/workflows/ci.yml/badge.svg)](https://github.com/TiagoR85/einvoice-api/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

I built `einvoice-api` as the API of the **einvoice-hub** family: an electronic
invoicing stack for the EU market. This repository is the REST entry point —
it issues scoped JWTs (OpenIddict client-credentials), stores invoices and
parties in Postgres, and publishes versioned CloudEvents to RabbitMQ through a
transactional outbox so the PDF worker (`einvoice-worker`) can process them
idempotently.

```mermaid
flowchart LR
    Client[Service client] -->|POST /v1/invoices (JWT)| API[einvoice-api]
    API --> PG[(PostgreSQL)]
    API --> Redis[(Redis\nidempotency + rate limit)]
    API -- outbox poll --> RMQ[RabbitMQ\neinvoice.events]
    RMQ --> Worker[einvoice-worker\nPDF + MinIO - next repo]
```

## About this repository

This is a **study project** from my public portfolio. I learn by building: I use **AI-assisted development tools** (agent-based pair programming) while holding the project to production standards - automated tests, CI quality gates, security scanning, review pipelines and documented architecture decisions.

The scenario is simple on purpose; the engineering is not.

## Quickstart

```bash
git clone https://github.com/TiagoR85/einvoice-api.git
cd einvoice-api
cp .env.example .env   # set POSTGRES_PASSWORD, OIDC_CLIENT_SECRET and OIDC_WORKER_CLIENT_SECRET
docker compose up --build
curl http://localhost:8080/health/ready
```

The API is healthy when `/health/ready` returns 200 (typically < 2 min after
the image build completes).

## Getting a token

```bash
curl -s http://localhost:8080/connect/token \
  -d grant_type=client_credentials \
  -d client_id=einvoice-client \
  -d client_secret=$OIDC_CLIENT_SECRET \
  -d "scope=invoice:read invoice:write"
```

## API reference (v1)

| Method | Path | Scope | Notes |
|---|---|---|---|
| POST | /v1/parties | invoice:write | Create customer/supplier |
| GET | /v1/parties | invoice:read | List parties |
| GET | /v1/parties/{id} | invoice:read | Fetch one |
| POST | /v1/invoices | invoice:write | Requires `Idempotency-Key` header; enqueues outbox event |
| GET | /v1/invoices | invoice:read | List invoices |
| GET | /v1/invoices/{id} | invoice:read | Fetch one with lines |
| GET | /health/live | - | Liveness |
| GET | /health/ready | - | Readiness (Postgres + Redis + RabbitMQ) |

## Running tests

```bash
dotnet test tests/Einvoice.Tests/Einvoice.Tests.csproj
```

Integration tests start PostgreSQL, Redis and RabbitMQ containers via
Testcontainers — Docker is required. CI enforces line coverage >= 80%.

## Architecture decisions

- [ADR-0001](docs/adr/0001-openiddict-client-credentials.md) — OpenIddict client-credentials JWT
- [ADR-0002](docs/adr/0002-transactional-outbox-cloudevents.md) — Transactional outbox + CloudEvents
- [ADR-0003](docs/adr/0003-modular-monolith.md) — Modular monolith
- [ADR-0004](docs/adr/0004-peppol-deferred.md) — Peppol deferred from v1

## Roadmap

- [x] v1: invoices + parties + outbox events
- [ ] Worker: PDF generation (iText) + MinIO storage + `invoice.completed`
- [ ] Blazor server-rendered UI for invoice list/creation
- [ ] Peppol access point (see ADR-0004)
- [ ] Demo stack on my VPS with docker compose profiles

## License

MIT — see [LICENSE](LICENSE).
