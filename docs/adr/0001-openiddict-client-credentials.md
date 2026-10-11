# ADR-0001: OpenIddict client-credentials for machine-to-machine JWT

Date: 2026-10-10

## Context

The API is consumed by other services (the PDF worker, integrations). I need
scoped authorization (invoice:read / invoice:write) without interactive logins.

## Decision

I use OpenIddict 7.7.2 with the OAuth 2.0 client-credentials flow. The API is
its own authorization server: it issues and validates JWTs locally (no external
IdP). Clients authenticate with a client id/secret seeded from environment
configuration; scopes map to ASP.NET Core authorization policies.

## Alternatives considered

- API keys: no standard token rotation, no scopes, weak for a portfolio piece.
- Hand-rolled JWT (Microsoft.IdentityModel): more code, more security risk,
  loses OIDC standard compliance signaling to recruiters.
- Keycloak as external IdP: correct for production multi-tenant, but adds an
  extra service to the demo and hides the .NET skills I want to showcase.

## Consequences

- Dev certificates are used for signing in demo; production would mount real
  certificates or rotate keys via configuration.
- The client secret must never be committed; it flows via Oidc__ClientSecret.
