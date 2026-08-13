# OWASP Top 10 (2021) — check reference for the threat-model audit

> Adapted from `zhongpei/autoresearch-skills` `/autoresearch:security` (MIT, © 2026 Udit Goenka).
> A static-analysis checklist: each item is a code-reading question, not a live probe. Map every
> finding to its `Axx` category (the ledger `owasp` field).
>
> **NEUTRAL BY DESIGN (S129 calibration control).** This file names NO StatsTid-specific finding or
> known hole — it is a generic category reference only. `.claude/skills/` is tracked and therefore
> present in the sweep worktree, so any enumeration of known/swept-unruled holes here would be an
> answer key the discovery/calibration agents could read (the leak class the S129 worktree isolation
> exists to prevent). Slice-specific scope and the revisit targets are provided by the Orchestrator
> **out of band** (prompt-embedded, revisit-slice only) — never in this tracked file.

## A01 — Broken Access Control
IDOR on parameterised routes; missing authorization on protected routes; horizontal / vertical
privilege escalation; directory traversal on file operations; CORS misconfiguration; missing
function-level access control; scope/tenant boundary bypass.

## A02 — Cryptographic Failures
Sensitive data in plaintext; weak hashing (MD5, SHA1); hardcoded secrets / API keys; missing
encryption at rest/in transit; weak randomness for tokens; exposed `.env` / config secrets.

## A03 — Injection
SQL/NoSQL injection; command injection; XSS (stored / reflected / DOM); template injection; LDAP
injection; path injection; header injection (CRLF).

## A04 — Insecure Design
Missing rate limiting; no account lockout; predictable resource IDs; race conditions (TOCTOU);
missing CSRF protection; insecure direct object references; missing revocation/invalidation windows.

## A05 — Security Misconfiguration
Debug mode enabled; default credentials; verbose errors / stack traces; missing security headers
(CSP, HSTS); unnecessary HTTP methods; directory listing; over-permissive service config.

## A06 — Vulnerable & Outdated Components
Known CVEs in dependencies; outdated frameworks; unmaintained dependencies; prototype-pollution-prone
packages. *(Static: read lockfiles/manifests; do not run `npm audit` against the live stack — record
the manifest evidence.)*

## A07 — Identification & Authentication Failures
Weak passwords; missing MFA; session fixation; JWT vulnerabilities (alg confusion, missing exp,
unverified signature, over-trusted claims); insecure password reset; missing session invalidation;
long token lifetimes with no revocation.

## A08 — Software & Data Integrity Failures
Missing CI/CD integrity checks; unsigned updates; insecure deserialization; missing CSP/SRI; unsigned
webhooks; secrets reachable from untrusted CI triggers.

## A09 — Security Logging & Monitoring Failures
Missing audit logs; no failed-auth logging; sensitive data in logs; missing alerting; log injection.

## A10 — Server-Side Request Forgery (SSRF)
Unvalidated URLs; DNS rebinding; missing allowlist; unvalidated proxy / forward endpoints; raw
credential/header forwarding to downstream services (confused deputy).
