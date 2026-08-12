# StatsTid Conventions

Cross-cutting conventions that bind **every** contributor — the Orchestrator and every domain agent
alike. Unlike the rest of the document map (which is routed selectively), **this file is included
verbatim in every agent prompt** — see CLAUDE.md "For the Orchestrator" step 5. Keep it short; it is
read on every task.

## The Invariant Model (how decisions are governed)

Work is governed by two DIFFERENT-IN-KIND sets: **invariants**, which are never traded away, and
**trade-offs**, which are balanced. (This replaces the former ranked 1–9 "priority order": most of
those items were things we would never actually trade, so ranking them against each other was
meaningless.)

**Inviolable invariants — co-equal, NOT ranked against each other.** A candidate solution that would
compromise ANY invariant is **not a valid path**: reject it and find another that satisfies all of
them. You do not sacrifice one to gain another. If two appear to conflict, find a design that
honours both; if they are genuinely, unavoidably in conflict (rare), it **escalates to the owner**
(a ruling or a requirement change) — never an ad-hoc trade by an agent.

- **Architectural integrity** — the design stays coherent; bounded-context and dependency rules
  hold. Deviations only by an explicit, documented owner ruling (the "known-accepted holes" mechanism).
- **Domain correctness** — the rule engine is deterministic and pure, and its results are correct,
  *including across OK-version transitions* (version correctness) and *at the payroll-integration
  boundary* (payroll correctness). A wrong result is the product failing at its one job.
- **Auditability** — every result is reconstructable and provable after the fact (event sourcing);
  the audit trail is never sacrificed for convenience.
- **Integration isolation & delivery** — bounded contexts stay isolated behind their contracts, and
  event delivery keeps its designed guarantees (exactly-once / per-stream ordering via the outbox).
- **Security & access control** — authentication, authorisation, org-scope validation, data
  confidentiality. Co-equal with domain correctness: a path that compromises *either* is invalid.

**Ranked trade-offs — balanced against effort and each other; NEVER above an invariant.** When these
conflict, the earlier one usually wins, all else equal.
1. **Usability & UX** — the product is usable. (Accessibility rises from "polish" to a genuine
   requirement as the design target firms up toward production.)
2. **Shipping cadence** — delivering working increments sustainably.

**Enforcement layer (not a priority).** CI/CD enforcement (build/test gates, doc-consistency, the
sprint-close guard) is *how* the invariants are kept true build-over-build. It protects the
invariants; it is not one of the goals being weighed.

## Project Status & Intent

**StatsTid is a learning project in active development — it is NOT deployed and NOT launching.**
Its purpose is to test how far AI-driven, multi-agent engineering can get toward a genuinely
production-grade system. The production-grade enterprise SaaS in CLAUDE.md's SYSTEM ROLE is the
**design target we build toward**, not a description of a live system: there are no real users, no
real payroll run, and no real personal data at risk.

What this means in practice:
- **Engineering discipline is the whole point and does not relax.** The governance, the invariant
  model above, the deterministic rule engine, event sourcing, dual-lens reviews, and adversarial
  verification are exactly what is being exercised — hold them to production standards.
- **Stakes framing is craft and correctness, not incident risk.** Do not describe findings or
  bugs in terms of real-world exposure ("data breach", "state-sector incident"). Severity labels
  rank engineering priority, not live danger.
- **"Launch" / "pre-launch" / "launch-blocking" in the docs mean readiness of the design target**
  — the bar a feature must clear to be considered production-ready — not a scheduled deployment.
  A "launch-blocking" item is one that must be correct before we would consider going live, which
  is a future intent, not a committed date.
- **The intent is to move toward production over time.** If that decision is ever taken, a
  dedicated hardening/cleanup pass (security disclosure, secrets, credentials, dependency audit)
  is required first — treat that as owed work, deferred by choice, not as done.

## Audience & Explanation Standard

**The product owner is a product manager, not the code's author — decisions and information must
be explained so a PM can understand them AND learn from them.** This is a first-class requirement
of the exercise (learning is its purpose), not a courtesy.
- **Lead with the "why" and the plain-language what, before the mechanism.** Name the problem, the
  decision, and its consequence in terms a PM follows; then go into file/line and implementation
  detail for the record.
- **Define or avoid jargon.** Domain terms (OK-version, ferieår, outbox, projection, STRIDE) and
  internal shorthand (FU-x, R-rulings, PAT/ADR/RES ids) get a one-line gloss on first use in any
  explanation, or a pointer to where they are defined.
- **Make trade-offs and alternatives legible.** When a choice is made, say what was given up and
  why — a PM learns from the reasoning, not just the outcome.
- **Governance artifacts serve this too.** Sprint logs, refinements, and the register carry a
  human-readable summary, not only citations; the person reading them should come away knowing
  more about the system than before.
