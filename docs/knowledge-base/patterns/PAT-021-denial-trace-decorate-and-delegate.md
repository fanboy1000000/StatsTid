# PAT-021 — Policy-denial trace: decorate-and-delegate, never decide

| Field | Value |
|-------|-------|
| **ID** | PAT-021 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S134 (QUAL-009 / SEC-038) |
| **Domains** | Security, Backend, Infrastructure, all HTTP hosts |
| **Tags** | authorization, denial-logging, audit, iauthorizationmiddlewareresulthandler, decorate-and-delegate, fail-open-observability, redaction |
| **Origin** | TASK-13403 (S134); `DenialLoggingAuthorizationResultHandler.cs`, `PolicyDenialClassifier.cs`, `PolicyDenialAuditRowMiddleware.cs` |

## The problem (plain language)

When ASP.NET's authorization layer refuses a request (403/401), it short-circuits the pipeline
BEFORE the app's audit middleware runs — so a denial historically produced **no log and no audit
row**, and the cause of a 403 could not be reconstructed afterward. Naively fixing this by writing
an audit row *inside* an authorization component risks the worse failure: observability code that
can change (or block) the authorization decision.

## The pattern — two halves, each doing exactly one job

1. **A decorating `IAuthorizationMiddlewareResultHandler`** (`DenialLoggingAuthorizationResultHandler`)
   wraps the framework default. It **observes** every final decision and **delegates the decision
   unchanged** — it decides nothing, blocks nothing, and its own failures are swallowed (a broken
   log line must never turn an allow into a deny or vice versa). On a denial it (a) emits ONE
   structured, redacted, correlation-linked log line, and (b) stashes a `PolicyDenialAudit` record
   (non-secret facts only: outcome, status, a stable non-PII policy id via `PolicyDenialClassifier`,
   route class, actor id/role, method, path — never claims/token/query/body) in `HttpContext.Items`.
2. **A denial-gated audit-row middleware** (`PolicyDenialAuditRowMiddleware`, registered BEFORE
   authorization so it survives the short-circuit) reads that Items record **after `_next`
   returns** and persists an `audit_log` row — only for admin/mutating route classes, only in hosts
   that register an `AuditLogRepository`, inside a swallowing try/catch (never touches the
   request's outcome or any export transaction).

**No double-write by construction:** the handler writes the log, the middleware writes the row —
one producer per sink, joined by the Items record and the correlation id (PAT-020).

## The rule

Observability of authorization must be a **decorator that delegates**: capture is post-decision,
the decision path is byte-identical with and without it, and every failure mode of the
observability code degrades to "less telemetry", never to a changed outcome.

## Related

- [PAT-020](PAT-020-ambient-correlation-id-request-scope.md) — the correlation id the denial line
  and audit row carry.
- SEC register (SEC-038) + `SPRINT-134.md` — the finding and the shipped wiring, incl. the
  Step-7a-reviewed middleware ordering across all 5 hosts.
