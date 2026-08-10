import { test, expect, type Locator, type Page } from '@playwright/test'
import { login } from './helpers/auth'
import { addMonths, monthWeekdays, runNonce } from './helpers/dates'

/**
 * S127 / TASK-12709 — the approval journey, REBUILT on a real month.
 *
 * ── Why this spec was rebuilt rather than patched ────────────────────────────
 *
 * The previous version built its entire fixture through the free-range "Indsend
 * periode" form on *Mine perioder*: one mid-month weekday, `Startdato ==
 * Slutdato`, and the resulting ONE-DAY period showed up in the manager's
 * Teamoversigt as that employee's month.
 *
 * Owner ruling R3 deleted that form, and the one-day trick it stood on is the
 * defect this sprint exists to close (§1 defect 3): period identity is the exact
 * date tuple, but the manager's overview resolves a period by OVERLAP — so a
 * single balanced weekday could represent a whole month to a leader. The old spec
 * was manufacturing a manager-visible period out of nothing.
 *
 * So the fixture is now the thing the product actually asks of an employee: a
 * WHOLE MONTH, registered and allocated day by day through the Skema page, sent in
 * ONE `POST /api/approval/send`. Nothing is seeded through the API or the
 * database — if the month is not really covered and really balanced, the send
 * returns 422 and this test goes red, which is exactly the gate under test.
 *
 * ── The journey ─────────────────────────────────────────────────────────────
 *
 *   1. emp001 (AC / STY01) opens the target month on the Skema page.
 *   2. For EVERY weekday of that month: open the day panel, register one work
 *      period (08:00–15:24 = 7,4 t) and allocate all 7,4 t to a real STY01
 *      project (DRIFT-01). Worked and allocated match per day, by construction.
 *   3. Send with "Godkend måned" → exactly one `POST /api/approval/send` → 200.
 *      The month locks (the backend locks `EMPLOYEE_APPROVED`; it never locked
 *      `SUBMITTED`), so the grid goes read-only — asserted, because that is the
 *      carried consequence of collapsing send and employee-approve into one call.
 *   4. mgr03 (emp001's unit leader, STY01) REJECTS the month from the
 *      Teamoversigt with a reason. Assertions follow owner rulings R1 and R7:
 *      the row's month-derived figures are WITHHELD once rejected, and the
 *      rejection reason is read at ROW level with the detail panel CLOSED.
 *   5. emp001 sees the rejection on the Skema footer and re-sends. The period row
 *      now exists, so this leg goes through the by-id adapter
 *      (`POST /api/approval/{periodId}/employee-approve`) — the second of the two
 *      route adapters over the one send command (§3.1).
 *   6. mgr03 APPROVES. The row flips to Godkendt, the SUBMITTED-only actions
 *      disappear and the R7 reason strip clears.
 *
 * Both manager verbs and both employee send adapters are exercised over ONE
 * month's registration. The old spec needed two months only because it could
 * fabricate a month per weekday; a real month is expensive, and rejection
 * conveniently hands the month back to the employee.
 *
 * ── Determinism across re-runs ───────────────────────────────────────────────
 *
 * The fixture's identity is now the (employee, year, month) tuple — there is no
 * day left to rotate, because the month IS the period. Two mechanisms keep re-runs
 * clean against the persistent local Postgres volume:
 *
 *   • the per-run nonce picks one of MONTH_WINDOW_SIZE months inside this spec's
 *     own forward window, so consecutive runs normally land on different months; and
 *   • that is only a first line of defence — the nonce recycles modulo the window —
 *     so the spec WALKS FORWARD from its nonce month until it finds one no prior run
 *     has consumed (`findUnusedMonth`). This is the month-level analogue of the old
 *     spec's day-walk, and it is bounded (MONTH_WALK_LIMIT).
 *
 * The walk's probe is the Skema footer, which is the page's own rendering of the
 * period state: a month offering "Godkend måned" with no "Afvist:" alert and no
 * "Frist:" deadline line has no period this test would disturb. The probe is a
 * heuristic and is not load-bearing on its own — the AUTHORITATIVE check is the
 * assertion that the send is a `POST /api/approval/send` returning 200. If the walk
 * ever picked a month that already held a period, that assertion fails loudly (the
 * page would route through the by-id adapter instead); it cannot pass weakly.
 *
 * CI runs against a fresh ephemeral volume, so the first candidate always wins there.
 *
 * ── Why this spec no longer shares helpers/dates' month window ───────────────
 *
 * skema-registration.spec.ts mutates the SAME employee and runs in PARALLEL (Playwright
 * runs files across workers), and it draws its month from `targetMonth`'s [1, 18] slots.
 * That was safe while this journey's periods were single days: the Skema save's lock
 * lookup is by EXACT (employee, period_start, period_end), so a one-day period could
 * never be found by a whole-month save. A whole-month period is exactly what that
 * lookup finds — and sending now locks the month (EMPLOYEE_APPROVED), so a shared month
 * would make the other spec's save fail. This spec therefore takes a window starting at
 * MONTH_WINDOW_START = 19 months out; the walk can extend it but never below 19, so the
 * two windows are DISJOINT BY CONSTRUCTION rather than by an argument about timing.
 */

// The Danish month labels both surfaces render (`lib/locale.formatMonthLabel`).
const DANISH_MONTHS = [
  'Januar', 'Februar', 'Marts', 'April', 'Maj', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'December',
]

const EMPLOYEE = 'emp001'
const LEADER = 'mgr03'

/**
 * A real STY01 project. TASK-12701a added DRIFT-01 / UDV-01 / ADM-01 / PROJ-01 to
 * the STY01 rows in the `init.sql` baseline precisely so this journey could exist —
 * before that, emp001's org had no project to allocate to and the allocation gate
 * was unsatisfiable through the UI. Selected by CODE (the day panel gives each
 * allocation input `id="alloc-<projectCode>"`), not by the Danish project name:
 * the code is the domain identity the seed guarantees.
 */
const PROJECT_CODE = 'DRIFT-01'

// One work period per day: 08:00–15:24 = 7 h 24 m = exactly 7,4 t. Chosen over a
// round 8 h because "7,4" is a distinctive string in the panel's own readouts
// ("= 7,4 t"), so the assertions cannot be satisfied by an unrelated "8".
const WORK_FROM = '08:00'
const WORK_TO = '15:24'
const DAY_HOURS_DA = '7,4'

const REJECTION_REASON = 'E2E afvisning — hele måneden skal gennemgås igen'

/**
 * This spec's forward month window, in whole months from the current UTC month.
 * DISJOINT from helpers/dates' `targetMonth` slots [1, 18] — see the header note on
 * the parallel skema-registration journey. The nonce picks a start inside
 * [19, 19 + MONTH_WINDOW_SIZE); `findUnusedMonth` may then walk up to
 * MONTH_WALK_LIMIT months further out, which only ever moves further from 18.
 */
const MONTH_WINDOW_START = 19
const MONTH_WINDOW_SIZE = 12

/** How many consecutive months the re-run walk may probe before giving up. */
const MONTH_WALK_LIMIT = 12

/** Stepper-click ceiling for the leader Teamoversigt, which has no month URL param.
 *  Must cover the whole reachable window: START + SIZE + WALK, with headroom. */
const TEAM_STEPPER_LIMIT = MONTH_WINDOW_START + MONTH_WINDOW_SIZE + MONTH_WALK_LIMIT + 12

/** A whole month of day-panel interaction — ~22 days × (open, two clock fields, one
 *  allocation, close) plus four logins — is far past Playwright's 30 s default. */
const JOURNEY_TIMEOUT_MS = 300_000

function monthLabel(year: number, month: number): string {
  return `${DANISH_MONTHS[month - 1]} ${year}`
}

/** Wait for the Skema month GET for exactly this (year, month). `useSkema` keeps the
 *  PREVIOUS month's data on screen while the next one is in flight, so probing the
 *  footer without this wait reads the wrong month. */
function skemaMonthResponse(page: Page, year: number, month: number) {
  return page.waitForResponse((resp) => {
    const url = new URL(resp.url())
    return (
      url.pathname === `/api/skema/${EMPLOYEE}/month` &&
      url.searchParams.get('year') === String(year) &&
      url.searchParams.get('month') === String(month)
    )
  })
}

/**
 * Open the Skema page directly on (year, month). SkemaPage seeds its initial month from
 * `?year=&month=` (the Årsoversigt drill-in), so no click-counting is needed on the
 * employee side — and no assumption about whether the page's "now" is UTC or local.
 *
 * The two waits are BOTH load-bearing, and the second is what makes `findUnusedMonth`
 * sound. `page.goto` remounts the page, and on a fresh mount SkemaPage renders ONLY the
 * "Indlæser skema..." spinner while `loading && !data` — the month heading is not in that
 * branch. So a visible heading proves the month's data has landed AND rendered, and no
 * probe below can read a previous month's footer. (The response wait alone would not:
 * a resolved fetch is not yet a committed render.)
 */
async function openSkemaMonth(page: Page, year: number, month: number): Promise<void> {
  const loaded = skemaMonthResponse(page, year, month)
  await page.goto(`/tid/registrering?year=${year}&month=${month}`)
  await loaded
  await expect(page.getByRole('heading', { name: monthLabel(year, month) })).toBeVisible()
}

/**
 * Walk forward from the nonce month until a month shows no sign of a period a prior
 * run left behind. Returns the month actually chosen.
 *
 * The three signals are the Skema footer's own branches (`SkemaPage.ApprovalFooter`):
 *   • "Godkend måned" present  ⇒ the month is NOT locked (EMPLOYEE_APPROVED and
 *     APPROVED both render a badge and no send button);
 *   • no "Afvist:" alert       ⇒ the month is not REJECTED;
 *   • no "Frist:" line         ⇒ no period row is supplying an employee deadline.
 *
 * (`getByText` is case-insensitive, so the third probe also matches the
 * "Lederfrist:" line — harmless: that line only renders in the EMPLOYEE_APPROVED
 * branch, which the first probe has already rejected.)
 */
async function findUnusedMonth(page: Page, start: { year: number; month: number }) {
  for (let step = 0; step < MONTH_WALK_LIMIT; step++) {
    const candidate = addMonths(start.year, start.month, step)
    await openSkemaMonth(page, candidate.year, candidate.month)

    const sendable = await page.getByRole('button', { name: 'Godkend måned' }).count()
    const rejected = await page.getByText('Afvist:').count()
    const hasDeadline = await page.getByText('Frist:').count()
    if (sendable === 1 && rejected === 0 && hasDeadline === 0) return candidate
  }
  throw new Error(
    `findUnusedMonth: every month in [${monthLabel(start.year, start.month)} .. +${MONTH_WALK_LIMIT}) ` +
      'already carries an approval period. The local Postgres volume needs a reset ' +
      '(docker compose down -v).',
  )
}

/**
 * Register one day THROUGH THE UI: open the day panel from the "Registrér arbejdstid"
 * cell, type the work period into step 1, allocate the same hours to `PROJECT_CODE` in
 * step 2, close with "Færdig".
 *
 * Both intermediate assertions are the panel's OWN arithmetic, not a restatement of the
 * input: "= 7,4 t" is the parsed period length (so a mistyped or unparsed clock value
 * renders "ugyldig" and fails here), and "Alt fordelt ✓" is `classifyAllocation`, the
 * frontend mirror of the backend allocation gate's tolerance. A day that reaches the
 * send unbalanced is therefore caught here, one day earlier and with the day named.
 */
async function registerAndAllocateDay(page: Page, isoDay: string): Promise<void> {
  await page.getByRole('button', { name: `Registrér arbejdstid ${isoDay}` }).click()

  const panel: Locator = page.getByRole('dialog', { name: /^Registrér tid/ })
  await expect(panel).toBeVisible()

  // Step 1 — the worked period. `.first()`: a day the app already holds periods for
  // renders one row per period, and we only ever write into the first. A second row
  // carrying hours does not slip through: it would push the day's WORKED total above
  // the 7,4 t we allocate, and "Alt fordelt ✓" below is a worked-vs-allocated verdict,
  // so the day would read "Resterende at fordele" instead and fail here.
  await panel.getByLabel('Fra', { exact: true }).first().fill(WORK_FROM)
  await panel.getByLabel('Til', { exact: true }).first().fill(WORK_TO)
  await expect(panel.getByText(`= ${DAY_HOURS_DA} t`)).toBeVisible()

  // Step 2 — allocate the whole day to one real project. The input is disabled until
  // step 1 has hours (R11), so `fill` also proves step 1 committed.
  await panel.locator(`#alloc-${PROJECT_CODE}`).fill(DAY_HOURS_DA)
  await expect(panel.getByText('Alt fordelt ✓')).toBeVisible()

  await panel.getByRole('button', { name: 'Færdig' }).click()
  await expect(panel).toBeHidden()
}

/** Drive the Teamoversigt month stepper onto `label`. The stepper holds its month in
 *  component state with no URL param, so it must be clicked — but it is driven to a
 *  LABEL rather than a click COUNT, which keeps this independent of whether the page
 *  initialises from local or UTC "now". */
async function stepTeamOversigtTo(page: Page, label: string): Promise<void> {
  const monthLabelEl = page.getByTestId('month-label')
  const next = page.getByRole('button', { name: /Næste/ })
  // Settle before the first read: a null textContent would be read as "not the target"
  // and spend a click before the stepper had rendered.
  await expect(monthLabelEl).toBeVisible()
  for (let i = 0; i <= TEAM_STEPPER_LIMIT; i++) {
    const current = ((await monthLabelEl.textContent()) ?? '').trim()
    if (current === label) return
    await next.click()
    // Settle before reading again: without this the loop can out-run React's
    // re-render, read a stale label and click straight past the target.
    await expect(monthLabelEl).not.toHaveText(current)
  }
  throw new Error(`stepTeamOversigtTo: never reached ${label}`)
}

/** Open the leader Teamoversigt on `label` and wait for THAT month's aggregate to land
 *  (the row keeps rendering the previous month while the next fetch is in flight). */
async function openTeamOversigt(
  page: Page,
  year: number,
  month: number,
): Promise<Locator> {
  await page.goto('/godkend/oversigt')
  await expect(page.getByRole('heading', { name: 'Teamoversigt' })).toBeVisible()

  const loaded = page.waitForResponse((resp) => {
    const url = new URL(resp.url())
    return (
      url.pathname === '/api/approval/team-overview' &&
      url.searchParams.get('year') === String(year) &&
      url.searchParams.get('month') === String(month)
    )
  })
  await stepTeamOversigtTo(page, monthLabel(year, month))
  await loaded

  const row = page.getByTestId(`team-row-${EMPLOYEE}`)
  await expect(row).toBeVisible()
  return row
}

/** The row's "Normtimer" cell — column 5 of the body row (checkbox, medarbejder,
 *  overenskomst, status, THIS). It renders `normRegistered` and is one of the five
 *  month-derived figures owner ruling R1 withholds from a REJECTED row; the withheld
 *  rendering is the em dash. */
function normCell(row: Locator): Locator {
  return row.locator('td').nth(4)
}

async function logOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Log ud' }).click()
  await expect(page).toHaveURL(/\/login$/)
}

test('emp001 registers and allocates a whole month in Skema, sends it, and mgr03 rejects then approves it', async ({
  page,
}) => {
  test.setTimeout(JOURNEY_TIMEOUT_MS)

  // ── Instrumentation: which approval routes fired, and did any save fail ──────
  // `handleApprove` FLUSHES the debounced saves before sending and silently returns
  // if a flush failed — collecting the save failures turns that silence into a named
  // assertion instead of an unexplained timeout on the send.
  const sendStatuses: number[] = []
  const employeeApproveStatuses: number[] = []
  const failedSaves: string[] = []
  page.on('response', (resp) => {
    const path = new URL(resp.url()).pathname
    if (path === '/api/approval/send') sendStatuses.push(resp.status())
    else if (/^\/api\/approval\/[^/]+\/employee-approve$/.test(path)) {
      employeeApproveStatuses.push(resp.status())
    } else if (path === `/api/skema/${EMPLOYEE}/save` && resp.status() !== 200) {
      failedSaves.push(`${resp.request().method()} ${path} → ${resp.status()}`)
    }
  })

  // ── 1. emp001 picks an unconsumed future month ──────────────────────────────
  await login(page, EMPLOYEE)

  // The seed month: a nonce-selected slot inside THIS spec's window (see
  // MONTH_WINDOW_START). Computed here rather than via `targetMonth`, whose [1, 18]
  // window belongs to the parallel skema-registration journey.
  const now = new Date()
  const seed = addMonths(
    now.getUTCFullYear(),
    now.getUTCMonth() + 1,
    MONTH_WINDOW_START + (runNonce() % MONTH_WINDOW_SIZE),
  )
  const { year, month } = await findUnusedMonth(page, seed)
  const weekdays = monthWeekdays(year, month)
  // Guards the helper, not the calendar: an empty or truncated day list would make
  // every loop below a no-op and the send's 422 would blame coverage instead. Any
  // Gregorian month holds between 20 (a 28-day February) and 23 weekdays — a bound
  // the old mid-month band (9 days) would have failed.
  expect(
    weekdays.length,
    `monthWeekdays(${year}, ${month}) returned ${weekdays.length} days`,
  ).toBeGreaterThanOrEqual(20)
  expect(weekdays.length).toBeLessThanOrEqual(23)

  // The precondition TASK-12701a exists to satisfy, asserted rather than assumed: a
  // real project in emp001's org, reachable from the day panel.
  await page.getByRole('button', { name: `Registrér arbejdstid ${weekdays[0]}` }).click()
  const firstPanel = page.getByRole('dialog', { name: /^Registrér tid/ })
  await expect(
    firstPanel.locator(`#alloc-${PROJECT_CODE}`),
    `${PROJECT_CODE} must be an active project in emp001's org (init.sql STY01 rows)`,
  ).toBeVisible()
  await firstPanel.getByRole('button', { name: 'Færdig' }).click()
  await expect(firstPanel).toBeHidden()

  // ── 2. Register + allocate EVERY weekday of the month ───────────────────────
  for (const isoDay of weekdays) {
    await registerAndAllocateDay(page, isoDay)
  }

  // Every registered day reads balanced in the grid's own work row (✓ = worked and
  // allocated agree within the gate's tolerance). This is the whole-month statement
  // the per-day panel assertions only made one day at a time.
  for (const isoDay of weekdays) {
    await expect(
      page.getByRole('button', { name: `Registrér arbejdstid ${isoDay}` }),
      `${isoDay} is not fully allocated in the grid`,
    ).toHaveText('✓')
  }
  expect(failedSaves, 'a Skema save failed during registration').toEqual([])

  // ── 3. Send the month — ONE POST /api/approval/send ─────────────────────────
  const sent = page.waitForResponse(
    (resp) =>
      new URL(resp.url()).pathname === '/api/approval/send' &&
      resp.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Godkend måned' }).click()
  const sendResp = await sent
  const sendBody = sendResp.status() === 200 ? '' : await sendResp.text()
  expect(sendResp.status(), `POST /api/approval/send → ${sendResp.status()} ${sendBody}`).toBe(200)
  expect(sendStatuses, 'the Skema send button must issue exactly one send').toEqual([200])

  // The carried consequence of R3 + the one-call collapse: sending goes straight to
  // EMPLOYEE_APPROVED, which the backend LOCKS (it never locked SUBMITTED). The grid
  // becomes read-only — correct behaviour, not a regression.
  await expect(page.getByText('Afventer leder godkendelse')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Godkend måned' })).toHaveCount(0)
  await expect(
    page.getByRole('button', { name: `Registrér arbejdstid ${weekdays[0]}` }),
    'the Skema must be read-only once the month is sent',
  ).toHaveCount(0)

  // ── 4. mgr03 rejects the month ──────────────────────────────────────────────
  await logOut(page)
  await login(page, LEADER)
  let empRow = await openTeamOversigt(page, year, month)

  // `exact: true` on every status-badge read, and it is load-bearing: `getByText` is
  // case-insensitive SUBSTRING matching by default, and the not-sent badge reads "Ikke
  // indsendt" — so a substring match for "Indsendt" would go green on precisely the
  // state this journey exists to move away from.
  await expect(empRow.getByText('Indsendt', { exact: true })).toBeVisible()
  // While the month IS sent, its month-derived figures are disclosed (the R1 control
  // half — without it the withheld assertion below could pass on an always-withheld row).
  await expect(normCell(empRow)).not.toContainText('—')
  // The manager's allocation warning agrees with the gate that let the month through.
  await expect(empRow.getByText('Manglende fordeling')).toHaveCount(0)

  await empRow.getByRole('button', { name: 'Afvis' }).click()
  const reasonBox = page.getByPlaceholder('Skriv en kort begrundelse til medarbejderen…')
  await expect(reasonBox).toBeVisible()
  await reasonBox.fill(REJECTION_REASON)
  await page.getByRole('button', { name: 'Afvis måned' }).click()

  await expect(empRow.getByText('Afvist', { exact: true })).toBeVisible()
  await expect(empRow.getByRole('button', { name: 'Godkend' })).toHaveCount(0)
  await expect(empRow.getByRole('button', { name: 'Afvis' })).toHaveCount(0)

  // Owner ruling R7 — the reason is read at ROW level, with the detail panel closed.
  // The second assertion is what makes the first mean anything: `canExpand` was NOT
  // relaxed for rejected rows, so the panel is genuinely unopened here.
  const reasonStrip = page.getByTestId(`team-rejection-${EMPLOYEE}`)
  await expect(reasonStrip).toBeVisible()
  await expect(reasonStrip).toContainText(REJECTION_REASON)
  await expect(page.getByTestId(`team-detail-row-${EMPLOYEE}`)).toHaveCount(0)

  // Owner ruling R1 — the month-derived figures are withheld once rejected.
  await expect(normCell(empRow)).toContainText('—')

  // ── 5. emp001 re-sends the rejected month (the by-id adapter) ───────────────
  await logOut(page)
  await login(page, EMPLOYEE)
  await openSkemaMonth(page, year, month)

  await expect(page.getByText(`Afvist: ${REJECTION_REASON}`)).toBeVisible()
  // A rejected month is unlocked again AND its registrations survived the round trip.
  await expect(page.getByRole('button', { name: `Registrér arbejdstid ${weekdays[0]}` })).toHaveText('✓')

  const resent = page.waitForResponse(
    (resp) =>
      /^\/api\/approval\/[^/]+\/employee-approve$/.test(new URL(resp.url()).pathname) &&
      resp.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Godkend måned' }).click()
  const resendResp = await resent
  const resendBody = resendResp.status() === 200 ? '' : await resendResp.text()
  expect(
    resendResp.status(),
    `POST /api/approval/{periodId}/employee-approve → ${resendResp.status()} ${resendBody}`,
  ).toBe(200)
  expect(employeeApproveStatuses, 're-send must use the by-id adapter exactly once').toEqual([200])
  // No second /send: the period exists now, so the by-id adapter owns this transition.
  expect(sendStatuses, 're-send must NOT create a second period').toEqual([200])
  await expect(page.getByText('Afventer leder godkendelse')).toBeVisible()

  // ── 6. mgr03 approves the re-sent month ─────────────────────────────────────
  await logOut(page)
  await login(page, LEADER)
  empRow = await openTeamOversigt(page, year, month)

  await expect(empRow.getByText('Indsendt', { exact: true })).toBeVisible()
  // R1 again, in the other direction: re-sending restores the withheld figures.
  await expect(normCell(empRow)).not.toContainText('—')
  await expect(page.getByTestId(`team-rejection-${EMPLOYEE}`)).toHaveCount(0)

  await empRow.getByRole('button', { name: 'Godkend' }).click()

  await expect(empRow.getByText('Godkendt', { exact: true })).toBeVisible()
  await expect(empRow.getByRole('button', { name: 'Godkend' })).toHaveCount(0)
  await expect(empRow.getByRole('button', { name: 'Afvis' })).toHaveCount(0)
})
