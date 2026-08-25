# PAT-020 — Ambient correlation id via request-scoped log scope + Items handoff

| Field | Value |
|-------|-------|
| **ID** | PAT-020 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S134 (QUAL-008 / QUAL-065) |
| **Domains** | Backend, Infrastructure, Security, all HTTP hosts |
| **Tags** | correlation-id, observability, logging-scope, includescopes, cross-service-trace, middleware |
| **Origin** | TASK-13402 (S134); `CorrelationIdMiddleware.cs`, `RuleEngineHeaderForwardingHandler.cs` |

## The problem (plain language)

A request's trail through the logs is only reconstructable if every log line it caused carries the
same id — including lines logged by code that never heard of correlation ids, and lines emitted by a
*different service* the request hopped to. Passing an id parameter through every call site does not
scale and rots instantly.

## The pattern

`CorrelationIdMiddleware` (first in the pipeline, every host) mints — or adopts an inbound
`X-Correlation-Id` header's — id per HTTP request and makes it ambient **two ways**:

1. **For logs:** it opens an `ILogger.BeginScope` carrying `CorrelationId` around the rest of the
   pipeline, so *every* log line emitted within the request carries the id automatically — zero
   per-call-site plumbing. Rendering in the default console sink requires `IncludeScopes = true` in
   the host's logging config (S134 set it tree-wide); structured sinks capture scopes regardless.
2. **For downstream code + outbound hops:** it writes the id to `HttpContext.Items` under
   `CorrelationIdMiddleware.ItemKey` and echoes it on the response header. The Backend→rule-engine
   `RuleEngineHeaderForwardingHandler` reads the Items value and stamps the outbound HTTP call, so a
   frontend-originated (header-less) request keeps ONE id across the service hop (QUAL-065) and the
   rule-engine host's own middleware adopts it.

## Honest scope (do not over-claim)

The scope covers only work inside the request pipeline. **Startup seeders, hosted
`BackgroundService`s, and outbox consumers run outside any request and are NOT covered** — their
logs carry no correlation id from this mechanism. If a background flow needs correlation, it needs
its own id source (e.g. the outbox row); do not "fix" it by widening this middleware.

## Related

- [PAT-005](PAT-005-period-calculation-service-http-rule-evaluation.md) — the Backend→rule-engine
  HTTP boundary this pattern's forwarder rides.
- ADR-026 audit projection — the audit rows that benefit from carrying the same id.
