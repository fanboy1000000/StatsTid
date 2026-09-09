# PAT-031 — Reuse the writer's valuation through a read-only entry point; never re-derive a legal quantity

| Field | Value |
|-------|-------|
| **ID** | PAT-031 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S140 |
| **Domains** | Backend, Infrastructure |
| **Tags** | valuation-reuse, single-implementation, legal-quantity, read-only-transaction, fail-closed, failure-isolation, additive-extraction, adr-033, pat-028 |

## Context

S140 needed a diagnostic list of employees who still had an untaken fifth week of holiday and no recorded §21
transfer agreement before 31 December. The quantity — days beyond four weeks, the §21/§24 tranche — is a **legal**
number the settlement *writer* already computes when it settles a holiday year (ADR-033). Two implementations of a
legal quantity will diverge, and the divergence will be discovered by a person whose holiday was mis-stated.

The obvious reuse target was a `private` method taking a transaction, with no `asOf` parameter and a fail-closed
throw per employee. None of that was accidental, and the pattern is about respecting it rather than working around it.

## The pattern

**Add an internal read-only entry point onto the writer's UNCHANGED code.**

- **Extract by addition, not by moving.** The S140 change was `+68 / −0` on the shared file: a new internal method
  that reads the subject and calls the existing private valuation with the read-shaped arguments. The writers call
  byte-identical code on a byte-identical path, so behaviour preservation is *provable by the diff* rather than
  argued, and **the existing settlement suites remain the pin** for both callers. A reviewer can verify it in one
  `--stat`.
- **Take no `asOf` when the operands are defined by the period, not by today.** The valuation's earned figure is
  valued at the holiday year's end and its planned figure comes from the balance row — both by design. "Today" only
  selects *which* period and *whether the window is open*; it is not an input to the arithmetic. Adding an `asOf`
  parameter to satisfy a caller's intuition would invent a degree of freedom the domain does not have. Say so at the
  entry point, so the next reader does not add one.
- **Take the caller's read-only transaction, take no lock, write nothing** — and **do not claim more isolation than you
  have.** The read path opens a transaction because the writer's signature requires one, and rolls it back; it acquires
  none of the advisory locks the write path needs, because it changes nothing. **It is NOT a consistent snapshot across
  the whole list**: in S140's implementation several supporting reads open their own connections outside that
  transaction, so a settlement committed mid-loop can tear one employee's figure relative to another's. The
  implementation says so in capitals at its own call site, and an earlier draft of *this pattern* claimed the opposite —
  caught by the Step-7a review. **A knowledge-base pattern is what the next implementer copies, so an over-claim here
  propagates into code.** State the isolation you actually provide; if a caller needs a coherent snapshot across
  subjects, that is a different design (one statement, or one transaction owning every read).
- **Isolate failure per subject.** A write path that fails closed per subject (missing dated history ⇒ throw) will
  throw inside a list. One employee's broken record must neither empty the list nor silently vanish from it: catch
  per subject, and surface a `cannotCompute` count with a **stable reason code** plus enough identification to chase
  it — never the exception text, which in this domain names dated anchors. Rethrow cancellation rather than swallowing
  it.
- **Report the population you excluded, and why.** S140's list excludes employees whose year is already settled
  (their days are disposed of) and leavers (whose days went through the termination path). Both exclusions prevent a
  tile from telling HR to record an agreement for days already handled — and both belong in the contract, because a
  reader cannot infer them from a count.

## Getting the period right is half the work

The list's target was **not** the holiday year containing today. With a September reset, the year whose §21 deadline
falls on *this* 31 December is the one that closed the previous August — so in November 2025 the answer is 2024, not
2025. Resolve it explicitly (`ResolveForYear(...)` selecting the year whose boundary falls in `today.Year`) and state
the entitlement **type** explicitly too, since a sibling type had a 30-April geometry that would have misfired
silently. The S140 implementer verified this by building a throwaway program against the real resolver rather than
reasoning about it — which is the right instinct for a date rule with an off-by-one-year failure mode.

## Consequences

- One implementation of the quantity, so the diagnostic list and the settlement cannot disagree.
- Correct before fast: the S140 read iterates candidates and calls the entry point per employee, one round trip each,
  declared in the file's own comment. That is a performance-register item if a page ever feels slow, not a reason to
  hand-roll a set-based copy of a legal calculation.
- A second implementation of a legal quantity is a **review BLOCKER**, not a style preference.

## Related

ADR-033 (vacation-settlement architecture — D5 the state machine, D8 the transfer-agreement record, and the §21/§24
tranche this reuses) · PAT-028 (compute today once) · PAT-026 (baseline at append, derive at read) · PAT-030
(enumerate the domain when absence is the signal — the sibling read in the same sprint) · QUAL-154 (the sprint's other
half).
