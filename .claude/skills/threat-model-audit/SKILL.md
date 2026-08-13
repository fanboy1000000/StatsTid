---
name: threat-model-audit
description: Static-analysis threat-model audit method (STRIDE + OWASP Top 10 + red-team personas) for the StatsTid S129 security sweep. INVOKE BY NAME ONLY — this is the sweep persona/method injected into read-only sweep agents; it does not auto-trigger, registers no hooks, and never modifies code.
---

# Threat-Model Audit — method for the StatsTid security sweep (S129 / WS5)

> **Provenance & vetting (S129, 2026-08-13).** This method is **adapted** from the
> `/autoresearch:security` mode of `zhongpei/autoresearch-skills` (MIT License, © 2026 Udit Goenka —
> `github.com/zhongpei/autoresearch-skills`). It is NOT the official Anthropic `autoresearch` plugin
> (which has no security mode). Per the S129 refinement's vetting verdict, the source was fetched and
> re-vetted: **MIT ✓, no lifecycle hooks ✓, no external network calls ✓**. Two source behaviours were
> **removed** in this adaptation: the `--fix` auto-remediation (the modify→verify "fix confirmed
> Critical/High" loop) and its self-owned report-folder output. **This sweep is READ-ONLY** — it
> discovers and records; it never edits code, and findings flow into the StatsTid SEC register +
> sweep ledger (not a plugin-owned folder).

## What this is
The threat-model **method** the S129 sweep applies: a systematic, static-analysis audit of the whole
attack surface, structured so findings are evidence-anchored and auditable. It is the persona the
Orchestrator injects into each fresh-context, read-only, capability-restricted sweep agent.

## Hard constraints (StatsTid governance — override anything in the source method)
- **Read-only / static analysis ONLY.** No live requests, no demo-credential use, no `psql`/`docker`
  invocation, no code modification. The machine hosts the owner's live native stack + demo DB. This is
  enforced by the agent tool profile (no Bash/PowerShell/psql/docker) and by running in a clean
  worktree pinned to the sprint-baseline SHA — not merely by this instruction.
- **No `--fix`, no auto-remediation.** Remediation is a *separate future sprint*, proposed from
  confirmed findings — never applied in-sweep.
- **Invoke by name only.** No auto-trigger, no hooks.
- **Findings land in the StatsTid artifacts**, not a plugin folder: the SEC register
  (`docs/operations/security-finding-register.md`, pointer-index) + each row's tracked
  `SPRINT-129.md` adjudication section + the gitignored sweep ledger (`results.tsv`). See the S129
  refinement (TASK-B/C/D) for the register form, the calibration control, the refute panel, and the
  slice partition.

## The method
The concrete audit process, the OWASP Top-10 checklist, the four red-team personas, the severity
scheme, and the ledger schema live in the two reference files beside this one:
- **`security.md`** — the 7-step audit process, STRIDE, the red-team personas, severity, the
  per-finding proof structure, the ledger schema.
- **`security-checklist.md`** — the OWASP Top-10 (2021) per-category check items.

## How the personas map to the S129 sweep slices
The source's four adversarial lenses align with the refinement's trust-boundary slices, so each slice
gets its natural persona (these are boundary/lens mappings only — no specific finding is named here,
per the calibration control):
- **Security Adversary** → slice (i) employee↔leader↔HR↔admin tiers (auth bypass, IDOR, injection,
  privilege escalation).
- **Supply-Chain Attacker** → slice (iii) deploy/CI/secrets (dependency CVEs, CI/CD weakness,
  secret exposure on untrusted triggers).
- **Insider Threat** → slice (i)/(ii) (what a low-privilege or lateral actor reaches; the org-scope
  and same-Organisation boundaries).
- **Infrastructure Attacker** → slice (iii)/(v) (compose, Dockerfiles, exposed ports, env vars;
  browser-side token storage / XSS sinks / proxy / CORS).
- Service↔service + token-forwarding (slice ii) draws on Security Adversary + Insider together.
