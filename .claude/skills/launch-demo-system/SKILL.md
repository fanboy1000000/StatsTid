---
name: launch-demo-system
description: Launch the full StatsTid system (docker-compose backend stack + Vite frontend + demo dataset) for manual UI testing or a demo. Trigger when asked to "start/run/launch the app/system", "bring it up for UI testing", "start a demo", or "let me click around". Produces a running app at http://localhost:3000 with seeded demo logins. This is the verified launch recipe — follow it instead of rediscovering compose/seed mechanics.
---

# Launch the StatsTid system for UI testing

Brings up the backend docker-compose stack, the Vite frontend, and the demo dataset, then hands the user a URL + logins. Verified end-to-end 2026-07-24 (S122).

## Access (what the user gets)
| | |
|---|---|
| **Open the app** | **http://localhost:3000** |
| Backend API | http://localhost:5100 (Vite proxies `/api` → here; see `frontend/vite.config.ts`) |
| Password (ALL users) | `password` |

**Demo logins** (full scale — 5 organisations, ~3,350 users). These mirror the dev-only "Test-personaer" panel on the login screen — one recommended login per role, all in the big org STYX1:
- `demo_styx1_0284` — Employee: Skema/tidsregistrering, Årsoversigt, Mine perioder
- `demo_styx1_0025` — LocalLeader: Godkend tid (Team-/Leder-oversigt), Vikariering
- `demo_styx1_0001` — LocalHR: Organisation & medarbejdere, Audit log
- `demo_styx1_0285` — LocalAdmin: Projekter, Brugerrettigheder, Lokal OK-konfiguration
- `demo_admin` — GlobalAdmin: Overenskomster, Lønartstilknytning + all admin surfaces
- Baseline `emp001` / `mgr03` / `admin01` / `ladm01` also work.

> 📅 **The rolling reseed (Step 2) puts activity in the PREVIOUS (last complete) month** so it never goes stale. Godkend tid / Mine perioder / Årsoversigt default to the *current* month, so switch back one month to see submitted periods. `demo_styx1_0025` is guaranteed a member awaiting approval there every reseed (curated); approvals are unit-scoped, so a leader whose unit has no submissions (e.g. `demo_styx1_0002`) shows an empty list.
>
> ⚠️ `demo_styx1_0005` is a **LocalLeader**, not an employee (an older version of this list mislabeled it). Use `demo_styx1_0284` for a plain-employee persona. There is one scoped `LOCAL_ADMIN` per org (`demo_styx2_0086`, `demo_styx3_0036`, …) for cross-org scope testing.

## Ports (docker-compose.yml)
postgres `5432` · backend-api `5100` · rule-engine `5200` · orchestrator `5300` · payroll `5400` · external `5500` · mock-payroll `5600` · mock-external `5700`. Frontend dev server `3000`.

## Prerequisites
- **Docker Desktop running** (`docker info` succeeds). If not: `Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"` and wait for `docker info`.
- **.NET SDK** (for the demo loader) and **node/npm** (for the frontend).
- **:5432 must be free.** The demo stack holds `5432`; it CANNOT coexist with the `:5432`-coupled fixed-port Regression tests (FAIL-002). If a compose Postgres is already up for tests, `down` it first.

## Step 1 — is the demo already loaded? (avoid an unnecessary wipe)
The rich demo SQL overlay only runs on a **fresh** Postgres volume (it's a `docker-entrypoint-initdb.d` script, runs once at init), but the `docker_postgres_data` volume PERSISTS across `down` (only `down -v` wipes it). So first check whether the current volume already has the demo:

```bash
# bring the stack up on the existing volume (non-destructive)
docker compose -f docker/docker-compose.yml up -d --build
# wait for health, then:
docker exec -i statstid-postgres psql -U statstid -d statstid -t \
  -c "SELECT count(*) FROM users WHERE user_id LIKE 'demo\_%';"
```
- **> 3000** → the demo is already loaded. Skip to **Step 3** (start the frontend). Done.
- **0** (only the 19-user baseline) → the demo is NOT loaded. Do **Step 2** (fresh reseed).

## Step 2 — fresh demo reseed (only if Step 1 shows 0 demo users, or you want a clean deterministic demo)
This WIPES the local Postgres volume (`down -v`). The data is fully regenerable (baseline from init.sql, demo from the deterministic loader), and this is the designed path ("demo regenerates on `down -v` + load").

```bash
# (a) generate a ROLLING manifest to a gitignored scratch path so the seeded activity lands in the
#     PREVIOUS (last complete) month instead of the pinned May 2026 — the committed structural SQL
#     (docker/postgres/99-demo-seed.sql) stays pinned + mounted; only the loaded manifest rolls.
#     (The user set / ids / active-leaver split are reference-date-independent, so this is consistent.)
mkdir -p tools/StatsTid.DemoSeed/.local
dotnet run --project tools/StatsTid.DemoSeed -- generate --scale full --reference-date rolling \
  --out tools/StatsTid.DemoSeed/.local/seed.sql \
  --manifest tools/StatsTid.DemoSeed/.local/demo-manifest.rolling.json
# (b) wipe + fresh up WITH the demo overlay (fresh init → init.sql then the demo SQL):
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml down -v
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up -d --build
# (c) wait until all services are healthy:
until [ "$(docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml ps \
  --format '{{.Status}}' | grep -c healthy)" -ge 8 ]; do sleep 5; done
# (d) run the API loader against the ROLLING manifest (trees, unit spines, roles, activity):
dotnet run --project tools/StatsTid.DemoSeed -- load --scale full \
  --manifest tools/StatsTid.DemoSeed/.local/demo-manifest.rolling.json \
  --base-url http://localhost:5100 --verify
```
For the pinned/committed dataset instead (activity in May 2026, byte-reproducible), drop `--reference-date rolling` and the scratch `--out`/`--manifest` and use the committed defaults. Use `--scale smoke` everywhere for a tiny ~30-user end-to-end instead of the full ~3,350.

**KNOWN-BENIGN verify exit 5:** `--verify` prints `VERIFICATION FAILED` and exits 5 on ONE check — `demo_admin` is homed at the MAO root `MINX` (org_type MAO), not an Organisation. That is correct for a global admin and does not affect the app. Every other check passes (org-trees single-rooted, unit counts exact, roles/homing clean). Treat a lone `demo_users_off_org=1` (== demo_admin) as green. **Do NOT pipe the loader through `tail`** — it masks the real exit code AND truncates the failing check line if a genuine failure occurs.

## Step 3 — start the frontend
```bash
npm --prefix frontend run dev    # → http://localhost:3000  (Vite; proxies /api to :5100)
```
Run it detached/backgrounded so it keeps serving; hot-reload is live for FE edits.

## Step 4 — drive it (confirm it actually works)
```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:3000/                       # 200 = frontend serves
curl -s -X POST http://localhost:5100/api/auth/login -H 'Content-Type: application/json' \
  -d '{"username":"demo_admin","password":"password"}' -w "\n%{http_code}\n"           # 200 + a JWT = backend+DB-auth+demo data OK
curl -s -X POST http://localhost:3000/api/auth/login -H 'Content-Type: application/json' \
  -d '{"username":"emp001","password":"password"}' -o /dev/null -w "%{http_code}\n"     # 200 = the Vite proxy path works
```
A JWT with real claims (role/org/scopes) proves the full chain (frontend serve → proxy → backend → DB auth → demo data). For a visual check, drive a browser (the repo has Playwright) to a logged-in screenshot.

## Teardown
```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml down      # stop, KEEP the demo data
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml down -v   # stop + WIPE (next up reseeds)
```
Always `down` the demo stack before running the `:5432`-coupled fixed-port Regression suites (FAIL-002).
