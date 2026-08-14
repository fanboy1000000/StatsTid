# StatsTid.DemoSeed — realistic demo/test dataset (S84)

A **deterministic** generator + API loader that produces a large, realistic **demo** dataset
for manual testing and demos: 1 demo MAO with 5 Organisations (1×~2,000, 1×~600, 3×~250 ≈
**3,350 employees**) with agreement/category/age/tenure spread, a light activity slice, and
~20–30 hand-curated "messy" cases.

> **Org model (S92 / ADR-035 flatten):** the ORG tree is 2 levels — **MAO** (`MINX`, root) →
> **Organisation** (`STYX1…STYX5`). There are NO AFDELING/TEAM org rows; every user sits
> directly on their Organisation (S103 / ADR-038 retired the legacy `enhed_label` model).
> The REPORTING tree keeps its realistic depth (a people-graph, independent of the org graph).

> **Unit spine (S114 / TASK-11400, ADR-038):** each demo Organisation additionally carries a
> DERIVED `units` tree exercising **all 5 types** (direktion › område › kontor › team › enhed):
> manager *m* at reporting-depth *d* anchors a unit of type `[d]`; *m* is HOMED in the unit it
> leads; the unit's members are *m* + *m*'s NON-manager reports; *m*'s manager-reports appear as
> CHILD units. The generated manager trees are DEPTH-FORCED to depths 0–4 exactly (per-org
> `UnitSpanOverride` in `ScaleConfig`; a generation-time assertion fails LOUDLY if any depth 0–4
> is unpopulated). Deliberate, counted messiness per org: ~2 leaderless units + ~3–5 cross-unit
> sideways-homed NON-manager members (disjoint units; the manifest ledger is verifier-asserted
> EXACTLY). The loader drives the REAL units admin APIs in the canonical order **units
> parent-first → home ALL members probe-first (fetched ETag) → appoint leaders LAST** (the D3
> re-home leadership strip + the leaders-422-non-members invariant make any other order wrong);
> re-runs are probe-first idempotent with ZERO expected 4xx.

> **This is DEMO data, fully isolated from the test fixture.** It is **opt-in** (a separate
> compose overlay), uses distinct ids (`MINX` / `STYX1…STYX5` / `demo_*`) and `DEMO_SEED`
> markers, and is **never mounted in CI**. The existing 19-user `init.sql` seed (emp001, mgr03,
> STY01…) is untouched, so the regression/smoke/e2e suites are unaffected.

## How to launch the rich demo stack

```bash
# 1. (once) generate the deterministic structural seed + manifest
dotnet run --project tools/StatsTid.DemoSeed -- generate --scale full

# 2. bring up the OPT-IN demo stack (fresh volume → init.sql then the demo seed)
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up -d --build
#    First boot is SLOW (a few minutes): the startup seeders create employee_profiles +
#    user_agreement_codes (+ events) for ~3,350 users, one row per transaction. This is
#    NOT a hang — watch: docker logs -f statstid-backend-api

# 3. load the reporting trees + activity via the real API (event-emitting + idempotent)
dotnet run --project tools/StatsTid.DemoSeed -- load --scale full --base-url http://localhost:5100 --verify

# 4. open the app
#    Frontend (vite):  npm --prefix frontend run dev   → http://localhost:3000
```

Use `--scale smoke` everywhere for a tiny (~30-user) end-to-end smoke of the whole pipeline.

**Recent-month activity (recommended for manual testing).** The steps above use the pinned
committed manifest (activity in May 2026, which goes stale as wall-clock advances). To make the
seeded activity land in the **previous (last complete) month** instead, generate a *rolling*
manifest to a scratch path and load that — the committed `99-demo-seed.sql` stays pinned + mounted:

```bash
mkdir -p tools/StatsTid.DemoSeed/.local
dotnet run --project tools/StatsTid.DemoSeed -- generate --scale full --reference-date rolling \
  --out tools/StatsTid.DemoSeed/.local/seed.sql \
  --manifest tools/StatsTid.DemoSeed/.local/demo-manifest.rolling.json
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml down -v
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up -d --build
dotnet run --project tools/StatsTid.DemoSeed -- load --scale full \
  --manifest tools/StatsTid.DemoSeed/.local/demo-manifest.rolling.json \
  --base-url http://localhost:5100 --verify
```
`.local/` is gitignored — the rolling artifacts are wall-clock-dependent and never committed. The
user set / ids / active-leaver split are reference-date-independent (RNG-driven), so the rolling
manifest loads cleanly onto the pinned SQL; only the activity month (and vikar dates) move.

## Demo logins (all share password `password`)

One recommended login per role (all in the big org STYX1) — these mirror the dev-only
"Test-personaer" panel on the login screen:

| Username | Role | Use for |
|----------|------|---------|
| `demo_styx1_0284` | EMPLOYEE | Skema/tidsregistrering, Årsoversigt, Mine perioder |
| `demo_styx1_0025` | LOCAL_LEADER | Godkend tid (Team-/Leder-oversigt), Vikariering |
| `demo_styx1_0001` | LOCAL_HR | Organisation & medarbejdere, Audit log |
| `demo_styx1_0285` | LOCAL_ADMIN | Projekter, Brugerrettigheder, Lokal OK-konfiguration |
| `demo_admin` | GLOBAL_ADMIN | Overenskomster, Lønartstilknytning + all admin surfaces |

> 📅 **The reseed puts the seeded activity in the PREVIOUS (last complete) month** via
> `--reference-date rolling` (see step 1 above), so it never goes stale. Godkend tid / Mine perioder
> / Årsoversigt default to the *current* month, so switch back one month to see registrations and
> submitted periods. The `LOCAL_LEADER` persona above (`demo_styx1_0025`) is **guaranteed** a member
> awaiting approval there every reseed (a curated guarantee — see `CurateDemoPersonas`); approvals
> are scoped to your own unit, so a leader whose unit has no submissions (e.g. `demo_styx1_0002`)
> shows an empty list. (The COMMITTED `99-demo-seed.sql` + manifest stay
> pinned at reference-date 2026-06-15 → activity May 2026, for reproducibility; only the reseed rolls.)
>
> ⚠️ `demo_styx1_0005` is a **LOCAL_LEADER**, not an employee — an earlier version of this table
> mislabeled it. Use `demo_styx1_0284` for a plain-employee persona. One scoped `LOCAL_ADMIN` is
> seeded per org (`demo_styx1_0285`, `demo_styx2_0086`, `demo_styx3_0036`, `demo_styx4_0036`,
> `demo_styx5_0037` — the 2nd active non-manager, gated by `ScaleConfig.CurateDemoPersonas`,
> full scale only) so the LocalAdmin-gated surfaces are reachable in the rich demo world.

(The baseline `emp001` / `mgr03` / `admin01` / `ladm01` etc. + password `password` still work too.)

## ⚠️ Ops warnings

- **The demo stack and the local `:5432`-coupled Regression tests cannot share the port.**
  Classes like `ReportingLineRepositoryTests` / `ManagerVikarEngineTests` connect to a hardcoded
  `localhost:5432` and assert baseline seed counts; with the demo stack loaded they will see the
  extra rows and report **false failures**. Run `docker compose -f docker/docker-compose.yml -f
  docker/docker-compose.demo.yml down -v` before running those tests locally. **CI is unaffected**
  (it uses its own services-postgres seeded from `init.sql` only; the demo overlay is never
  referenced in `.github/workflows/ci.yml`).
- **Init scripts only run on a FRESH volume.** To reload the demo data, `down -v` first.
- **Container init ordering:** the seed is mounted as `zz-demo-seed.sql` (NOT `99-`): the Postgres
  entrypoint runs `/docker-entrypoint-initdb.d/*` in byte-lexical order, where `'9'` (0x39) sorts
  **before** `'i'` in `init.sql` (0x69) — so a `99-` prefix would run against a schema-less DB.
  `zz-` sorts after `init.sql`. (The on-disk artifact keeps the name `99-demo-seed.sql`.)

## Determinism

Same `--seed` (default 42) + `--scale` + `--reference-date` ⇒ byte-identical `99-demo-seed.sql` +
manifest. All dates derive from the reference date, not wall-clock — the generator itself never
reads the clock. The one opt-in exception is `--reference-date rolling`, resolved (in
`ReferenceDateResolver`, at the CLI layer only) to the first of the current month so the reseed's
activity lands in the previous month; that run is intentionally NOT byte-reproducible and is used
for the local demo, never to regenerate the committed artifacts. The **structural** layer is reproducible;
the **activity** layer (API-driven) is **idempotent** (skip-if-present, no duplicates on re-run)
but not byte-reproducible (server-stamped event ids/timestamps reflect generation wall-clock).

The S114 unit post-pass consumes a **second derived** `Random(seed ^ salt)` — it never touches
the primary `_rng` stream, so the people/edges/activity/roles/profiles output for a NO-override
config is **byte-identical to the pre-S114 generator**, pinned by
`tests/StatsTid.Tests.DemoSeed/Golden/` (captured from the pre-change generator) +
`GoldenLegacyPinTests`. The unit spine itself is fully deterministic per (seed, scale).

## Known limitation (S84)

Privileged role grants (LOCAL_HR / LOCAL_LEADER / LOCAL_ADMIN) are **SQL-seeded event-less** rather
than granted via `POST /api/admin/roles/grant`, because that endpoint has a **pre-existing production bug**
(it inserts `role_assignment_audit.action='GRANT'` but the schema CHECK only allows `'GRANTED'` →
every call 500s). The reporting **trees** still load via the event-emitting bulk-import API. See the
S84 sprint log for the recorded follow-up to fix the grant endpoint.
