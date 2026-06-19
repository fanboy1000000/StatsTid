# StatsTid.DemoSeed — realistic demo/test dataset (S84)

A **deterministic** generator + API loader that produces a large, realistic **demo** dataset
for manual testing and demos: 5 styrelse trees (1×~2,000, 1×~600, 3×~250 ≈ **3,350 employees**)
with realistic hierarchy, agreement/category/age/tenure spread, a light activity slice, and
~20–30 hand-curated "messy" cases.

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

## Demo logins (all share password `password`)

| Username | Role | Use for |
|----------|------|---------|
| `demo_admin` | GLOBAL_ADMIN | admin surfaces, the import/role tooling |
| `demo_styx1_0002` | a manager in the big org | approval dashboard, vikar |
| `demo_styx1_0005` | an employee in the big org | Skema / MyPeriods |

(The baseline `emp001` / `mgr03` / `admin01` etc. + password `password` still work too.)

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

Same `--seed` (default 42) + `--scale` ⇒ byte-identical `99-demo-seed.sql` + manifest. All dates
derive from a fixed `--reference-date`, not wall-clock. The **structural** layer is reproducible;
the **activity** layer (API-driven) is **idempotent** (skip-if-present, no duplicates on re-run)
but not byte-reproducible (server-stamped event ids/timestamps reflect generation wall-clock).

## Known limitation (S84)

Privileged role grants (LOCAL_HR / LOCAL_LEADER) are **SQL-seeded event-less** rather than granted
via `POST /api/admin/roles/grant`, because that endpoint has a **pre-existing production bug**
(it inserts `role_assignment_audit.action='GRANT'` but the schema CHECK only allows `'GRANTED'` →
every call 500s). The reporting **trees** still load via the event-emitting bulk-import API. See the
S84 sprint log for the recorded follow-up to fix the grant endpoint.
