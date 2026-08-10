# PAT-018 — Danish negative-space status labels defeat default substring text matching

| Field | Value |
|-------|-------|
| **ID** | PAT-018 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S127 |
| **Domains** | Frontend, Test |
| **Tags** | e2e, playwright, testing-library, false-green, danish-labels, exact-match, hollow-assertion |
| **Origin** | TASK-12709 (S127) |

## The trap

Playwright's `getByText()` and Testing Library's `getByText()` default to **case-insensitive substring**
matching. Danish status vocabulary in this codebase forms its negatives by **prefixing**, not by using a
different word:

| Positive | Negative |
|---|---|
| `Indsendt` | **`Ikke indsendt`** |
| `Godkendt` | **`Ikke godkendt`** |
| `Fordelt` | `Ikke fordelt` |

So:

```ts
await expect(page.getByText('Indsendt')).toBeVisible()
```

**passes against `"Ikke indsendt"`** — i.e. it goes green on exactly the state the test exists to prove
the app has moved *away from*. Verified live: `StrukturPanel.tsx:1314` renders `Ikke indsendt`.

This is the S125 hollow-assertion family in a new medium — it looks like evidence and is not.

## The rule

**Any status assertion whose label is a prefix or suffix of its own negation must use exact matching.**

```ts
await expect(page.getByText('Indsendt', { exact: true })).toBeVisible()
// or anchor on a testid / role+name rather than free text
```

Before writing a text assertion on a Danish status, grep for the `Ikke …` / `Ingen …` form of the same
word. If one exists, `exact: true` is mandatory.

## Detection

```
grep -rn "Ikke \|Ingen " frontend/src --include=*.tsx
```
Every hit is a word whose positive form is unsafe to assert by substring.

## Related

- [PAT-014](PAT-014-characterization-baseline-one-inversion-per-encoding.md) — prove the assertion can
  fail; a hollow matcher is the same defect one layer down.
- [PAT-016](PAT-016-container-predicate-silently-gates-its-contents.md) — the other S127 instance of a
  green test that could not see the thing it named.
