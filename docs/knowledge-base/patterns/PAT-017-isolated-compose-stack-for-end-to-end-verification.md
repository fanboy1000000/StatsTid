# PAT-017 — An isolated compose stack makes "needs a running stack" a non-excuse

| Field | Value |
|-------|-------|
| **ID** | PAT-017 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S127 |
| **Domains** | Ops, Tooling, Test |
| **Tags** | docker-compose, isolation, end-to-end, fixed-port, verification, fail-002 |
| **Origin** | TASK-12701b (S127) |

## The problem it solves

Two standing constraints used to make live end-to-end verification "impossible", so claims got downgraded
to *argued* instead of *observed*:

- **FAIL-002** — the demo/live stack owns `:5432`, so fixed-port suites must not run against it.
- The owner may be using the demo stack, so it must not be torn down or mutated.

Both dissolve if you stand up a **second, fully isolated stack** beside it.

## The recipe

```
docker compose -p <unique-project> -f docker/docker-compose.yml -f <isolation-override>.yml up -d --build
```

The override needs, per service:

- a distinct `container_name`
- **`ports: !override`** — see the trap below
- distinct volumes (a distinct `-p` project name gives you this for free)

Then point the tool under test at the new ports (`--base-url`, `--db-conn`).

## ⚠ The trap that costs the first attempt

**Compose MERGES `ports` by appending.** A plain override does not replace the base mapping — it adds to
it, so the stack still tries to bind the original port and dies with
`Bind for 0.0.0.0:5200 failed: port is already allocated`.

The **`!override`** YAML tag is required to replace rather than append.

## What it bought in S127

The sprint's headline precondition — *"375 approval periods, exactly 1 passes the coverage gate"* — was
measured at refinement time and had to become *375 of 375* for the sprint to be deployable. That claim
was going to ship as "verified in SQL, not through a live load."

Instead: a fresh full-scale load (`sent=375 … failures=0`, all verifier arms exact, exit 0), a rerun, and
a deliberately-broken-world run that exited 6 with the failing row named. The owner's stack was confirmed
untouched before and after; all isolated containers, images and volumes were removed afterwards.

Cost: one build plus two loads.

## When to reach for it

- Verifying a loader/migration/seed end to end
- Any fixed-port suite while a stack is up (the FAIL-002 class)
- Deliberately corrupting state to prove a check fires — never do that against the owner's world

## Hygiene

- [ ] Unique `-p` project name; nothing shares a container name with the live stack
- [ ] `ports: !override` on every published service
- [ ] Confirm the live stack's state **before and after** (a row-count query is enough)
- [ ] Remove containers, images and volumes when done; verify none remain
