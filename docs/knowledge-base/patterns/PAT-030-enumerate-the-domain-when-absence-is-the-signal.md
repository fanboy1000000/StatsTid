# PAT-030 — Enumerate the domain, don't query the rows, when absence is the signal

| Field | Value |
|-------|-------|
| **ID** | PAT-030 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S140 |
| **Domains** | Backend, Infrastructure |
| **Tags** | diagnostic-list, generate-series, left-join, absence-as-signal, lookback-floor, deadline-source, partition, summary-count, pat-026, adr-040 |

## Context

S140 built HR's "months past deadline" and "leaver's final month" lists. Both are about **something that did not
happen** — a month nobody submitted, a final month nobody approved. An `approval_periods` row is created only when a
month is *sent* (one production writer), and the persisted statuses are DRAFT / EMPLOYEE_APPROVED / SUBMITTED /
APPROVED / REJECTED — **there is no `OPEN`**. So "never sent" is the *absence of a row*, and any
`SELECT … FROM approval_periods` is structurally blind to the single most important case.

The same shape recurs wherever a row is created lazily by a user action: the thing you must report is the gap between
the domain and the table.

## The pattern

**Generate the domain, left-join what exists, and give absence an explicit name.**

1. **Generate** the domain the report is about — here, the months each employment window covers, via `generate_series`
   from a ruled floor to the current month.
2. **`LEFT JOIN`** the rows that exist, on an exact key (S140 joined `period_type = 'MONTHLY' AND period_start = …
   AND period_end = …`, served by the `UNIQUE (employee_id, period_start, period_end)` index, so a weekly period
   inside the month can never be mistaken for the month's row).
3. **`COALESCE` the missing state to an explicit sentinel** — `COALESCE(status, 'NONE')` — so no predicate silently
   evaporates to NULL. A bare `status <> 'APPROVED'` evaluates to NULL for a missing row and **drops the very case
   the report exists to surface**.
4. **Serve every classification from that one statement** with parameter-driven predicates.

Two properties fall out for free, and they are the reason to do it this way:

- **Sibling lists drawn over the SAME population partition by construction, not by agreement.** Two lists built from one
  enumeration with complementary status predicates over the same rows — S140's "employee late" and "approver late" — cannot
  both contain the same row, and nothing can fall between them. Two separately written predicates that *happen* to agree
  are exactly the class of bug this project keeps finding. **Read the scope of that claim carefully:** it holds only while
  the population is shared. S140 also has two lists that are **disjoint but not complementary** (one requires APPROVED, the
  other requires not-APPROVED, so no month is on both) whose *populations differ* — one is restricted to each leaver's
  final month by owner ruling, the other spans everyone — and the sprint had to **retract** a contract that claimed they
  partitioned. See the guard-rail below; do not carry the stronger reading away from this paragraph.
- **A summary count cannot disagree with its own list.** Implement `?summary=true` by omitting the list from the
  **wire**, not by writing a second `COUNT(*)` query — a second copy of the predicate is a second thing to keep in
  sync. Count ≡ list length, always.

## Guard-rails

- **Bound the enumeration with an explicitly ruled floor, and echo the floor on the response.** An unbounded
  enumeration of things that did not happen is unusable on day one (every pre-adoption month of every long-tenured
  employee); a bounded one that does not *say* it is bounded is dishonest. S140's floor is a rolling 12 months by
  owner ruling, and every response carries `lookbackFloor` so a tile can state its own reach.
- **Never treat a missing deadline as a met one.** Compute the ratified default and **flag it**
  (`deadlineSource: "stored" | "computed"`), so a reader can always tell a recorded deadline from a derived one.
  Silently reading NULL as "on time" is the same overclaim as a tile captioned "past deadline" that counts "pending".
- **Bound by the DOMAIN fact, never by an operational flag.** Use the employment window (ADR-040 D2, NULL = unbounded),
  not `is_active`, which governs login only (D3). Excluding inactive users would hide precisely the leaver months the
  report exists to surface. Note the residual this leaves — an employee deactivated *without* an end date accrues for
  ever (QUAL-167) — and register it rather than inventing a suppression rule.
- **State overlaps that are real.** A leaver's late final month legitimately appears on two lists with two different
  accountable roles, so the counts are **not additive**; say so in the response contract, not only in a comment.
- **Distinguish disjoint from complementary.** Two lists can be unable to share a row (one requires APPROVED, the other
  requires not-APPROVED) while still being drawn over *different base populations* — S140's leaver list is
  final-month-only by ruling, its not-exported list spans everyone. Disjoint ≠ complementary, and claiming the stronger
  property is a documentation defect the external lens will find.

## Cost characteristic

The enumeration is O(employees in scope × months in the floor) rows before filtering — roughly 26,000 for a
2,000-person organisation over 13 months. Bounded by the floor, filtered in SQL so few rows return, joined on a unique
index. "Correct first, fast later" was the deliberate call; if a landing page ever feels slow, that belongs in the
performance register rather than in a premature aggregate.

## Related

PAT-026 (baseline at append, derive at read — the sibling technique for an honest diagnostic list over facts owned
elsewhere) · PAT-028 (compute today once) · ADR-040 D2/D3 (the employment window; `is_active` governs login) ·
ADR-034 D4 (the read-only cross-context lookup one of these lists depends on) · QUAL-163 (the mislabelled tile this
pattern replaces) · QUAL-167 (the registered residual).
