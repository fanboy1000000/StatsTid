# Threat-Model Audit — the method (STRIDE + red-team, read-only)

> Adapted from `zhongpei/autoresearch-skills` `/autoresearch:security` (MIT, © 2026 Udit Goenka),
> `--fix`/auto-remediation removed, output redirected to the StatsTid SEC register + ledger. READ-ONLY.

## Setup phase — threat-model generation (steps 1–7)

**Step 1 — Codebase reconnaissance.** Scan the (worktree, baseline-SHA) project to build context:
package/project manifests, `.env.example` / appsettings, Dockerfiles + compose, API route/endpoint
files, auth/middleware, DB schema (`init.sql`, generated db-schema), CI/CD configs.

**Step 2 — Asset identification.** Catalogue every security-relevant asset: data stores,
authentication material (JWT secret, claims), API endpoints, external service integrations, user-input
surfaces, configuration/secrets, static assets.

**Step 3 — Trust-boundary mapping.** Identify where trust level changes: Browser↔Server,
Server↔Database, Server↔External APIs, public↔authenticated routes, Employee↔Leader↔HR↔Admin roles,
service↔service, CI/CD↔Production, Container↔Host. (These are the S129 sweep slices.)

**Step 4 — STRIDE threat model.** For each (asset × trust boundary), analyse: **S**poofing,
**T**ampering, **R**epudiation, **I**nformation disclosure, **D**enial of service, **E**levation of
privilege.

**Step 5 — Attack-surface map.** Entry points, data flows, and abuse paths — naming specific
endpoints and chaining vectors.

**Step 6 — Baseline.** Record existing known issues before the loop as iteration #0. For StatsTid the
"baseline" (the revisit set) is provided by the Orchestrator **out of band** to the revisit-slice
(iv) agents ONLY — it is NOT enumerated in this tracked method file, because the discovery/calibration
slices must not receive the known-hole list (the calibration control; see the S129 refinement).

**Step 7 — Results log.** Initialise the ledger `results.tsv`. **StatsTid ledger schema** (extends the
source's `iteration | vector | severity | owasp | stride | confidence | location | description` with
the S129 governance fields): `iteration | slice | vector | severity | owasp | stride | confidence |
location(file:line) | description | new-vs-dup(SEC-NNN) | sources-consulted | no-finding-because`.
The `sources-consulted` field enforces the calibration provenance rule (a rediscovery counts only
when code-anchored, citing no answer-bearing artifact); only the Orchestrator writes this file and
assigns SEC ids.

## Loop mechanics (per iteration — read-only)
1. Review the threat model + prior ledger rows (prompt-embedded).
2. Select the next untested attack vector for this slice.
3. Analyse the target code for the vulnerability (static; trace every input to its sink, find the
   missing guard).
4. **Validate** (proof construction) — see below.
5. Classify severity + OWASP/STRIDE, log the row (return to the Orchestrator).
6. Repeat until the slice's coverage cells are examined or the iteration budget/interrupt.

## Red-team adversarial lenses (personas)
- **Security Adversary (primary)** — "I'm a hacker breaching this system." Auth bypass, injection,
  data exposure, privilege escalation. Trace every input to its sink; find missing guards.
- **Supply-Chain Attacker** — "I'm compromising dependencies or the build pipeline." Dependency CVEs,
  CI/CD weakness, unsigned artifacts, secret exposure on untrusted triggers.
- **Insider Threat** — "I'm a malicious/compromised low-privilege account." Privilege escalation, data
  exfiltration, access-control gaps; what a low-privilege or lateral actor can reach.
- **Infrastructure Attacker** — "I'm attacking the deployment, not the code." Container config,
  exposed services/ports, network segmentation, env vars.

## Validate — per-finding proof structure (the evidence bar)
```
Finding proof:
  ├── Vulnerable code location (file:line)
  ├── Attack scenario (step-by-step)
  ├── Input that triggers the vulnerability
  ├── Expected vs actual behaviour
  ├── Impact assessment
  └── Confidence (Confirmed / Likely / Possible)
```
Confidence rules: **Confirmed** — code path clearly allows the attack, no guards present. **Likely** —
guards exist but are bypassable/incomplete. **Possible** — theoretical, depends on config/runtime.
(Every finding requires code evidence — no evidence, no finding. This feeds the refute panel: a
CONFIRMED candidate is handed to a fresh refuter given ONLY claim+evidence.)

## Severity (CVSS-inspired — engineering-priority ranking, NOT live-incident claims)
> StatsTid is a hobby/learning project; severity ranks engineering priority, not real-world exposure.
- **Critical** — RCE, auth bypass, SQL injection, data breach, admin takeover.
- **High** — stored XSS, SSRF, privilege escalation, mass data exposure.
- **Medium** — CSRF, open redirect, info disclosure, missing rate limits.
- **Low** — missing security headers, verbose errors, weak session config.
- **Info** — best-practice / hardening suggestions.

## What this method does NOT do (removed from the source)
- No `--fix`, no modify→verify remediation loop, no code writes. Remediation is a separate proposed
  sprint built from confirmed + overturned findings.
- No plugin-owned report folder — output is the StatsTid SEC register + tracked SPRINT-129
  adjudication sections + the gitignored sweep ledger.
- No live tool execution against the running stack (`npm audit`/`pip audit`/`go vuln` are noted as
  evidence sources only where a lockfile/manifest can be read statically; no process is spawned).
