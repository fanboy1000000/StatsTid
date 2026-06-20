import { test, expect, type Page, type Locator } from '@playwright/test'
import { login } from './helpers/auth'
import { runNonce, targetMonth, nonceWeekdayIndex, rotatedWeekdays } from './helpers/dates'

/**
 * SPRINT-87 / TASK-8702 (was SPRINT-82 journey 3): the approval approve + reject
 * chain, REWRITTEN for the new leader Teamoversigt at /godkend/oversigt.
 *
 * The TeamOversigt is a PER-EMPLOYEE aggregate (one row per employee in the
 * leader's designated-act-authority set for the selected month — keyed on
 * employeeId, NOT periodId). mgr03's STY01 tree has exactly ONE report, emp001
 * (init.sql reporting_lines), so emp001 is the single team row. Because the page
 * collapses to one row per (employee, month), the approve and reject verbs are
 * exercised on TWO DISTINCT nonce MONTHS (each holding one emp001 period) rather
 * than two period rows in one month:
 *
 *   • approve month  (offset 0): emp001 submits a SUBMITTED period there →
 *     mgr03 steps the month stepper onto it → APPROVES the emp001 row
 *     (Indsendt → Godkendt; the SUBMITTED-only action buttons disappear).
 *   • reject month   (offset 9): emp001 submits another SUBMITTED period →
 *     mgr03 steps onto it → REJECTS via the kit reject dialog
 *     (Indsendt → Afvist; the action buttons disappear).
 *
 * targetMonth(nonce, 0) and targetMonth(nonce, 9) always resolve to DIFFERENT
 * months (slots differ: 9 ≢ 0 mod 18), so the two periods never collide and each
 * month's team-overview shows emp001 in the expected single status.
 *
 * Re-run tolerance: the per-run nonce targets unique future months + a
 * nonce-rotated starting day; the resilient submit walks the month's mid-month
 * weekdays until it finds a FREE day (stepping over any period a prior run left on
 * a recurring slot in the persistent local volume — the /submit guard refuses
 * re-submit, ApprovalEndpoints.cs:109). CI's fresh ephemeral volume always takes
 * the first candidate.
 *
 * Assertions are DISCRIMINATING: the emp001 row is located by a stable
 * data-testid (team-row-emp001); the test asserts its STATUS BADGE transitioned
 * (Indsendt → Godkendt / Afvist) AND the SUBMITTED-only action buttons are gone —
 * not merely that a button was clickable.
 */

const DANISH_MONTHS = [
  'Januar', 'Februar', 'Marts', 'April', 'Maj', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'December',
]

// Two distinct slots from the same per-run nonce → two distinct months.
const APPROVE_MONTH_OFFSET = 0
const REJECT_MONTH_OFFSET = 9

/**
 * Submit a raw SUBMITTED single-day period for the logged-in emp001 via the
 * MyPeriods form, walking the ordered `candidateDays` until one yields a clean
 * SUBMITTED period; returns the day actually used.
 *
 * Resilience: on the persistent LOCAL volume a candidate day may already hold a
 * period from a prior run → /submit 409s. We STEP to the next candidate (≤ 9 in
 * a month). On CI's fresh ephemeral volume the first candidate always wins.
 */
async function submitPeriodViaForm(page: Page, candidateDays: string[]): Promise<string> {
  await page.goto('/tid/mine-perioder')
  await expect(page.getByRole('heading', { name: 'Mine perioder' })).toBeVisible()

  for (const isoDay of candidateDays) {
    await page.getByLabel('Startdato').fill(isoDay)
    await page.getByLabel('Slutdato').fill(isoDay)
    // Periodetype + Overenskomst default to WEEKLY / AC (valid for emp001).

    const submitResp = page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/approval/submit') && resp.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Indsend periode' }).click()
    const resp = await submitResp

    if (resp.status() === 200) {
      await expect(page.getByText('Periode indsendt.')).toBeVisible()
      return isoDay
    }
    // 409 ("Period already exists ...") on a recurring local slot → try the next day.
  }
  throw new Error(
    `submitPeriodViaForm: no free candidate day among ${candidateDays.join(', ')}`,
  )
}

/** Drive the TeamOversigt month stepper FORWARD `clicks` whole months from the
 *  current UTC month onto the target (the stepper initialises to the current UTC
 *  month, one "Næste →" click per month, internal state, no URL param). */
async function stepOversigtToMonth(page: Page, clicks: number, monthLabel: string): Promise<void> {
  const next = page.getByRole('button', { name: /Næste/ })
  for (let i = 0; i < clicks; i++) {
    await next.click()
  }
  await expect(page.getByTestId('month-label')).toHaveText(monthLabel)
}

test('mgr03 approves one emp001 month and rejects another from the Teamoversigt', async ({ page }) => {
  const nonce = runNonce()
  const approve = targetMonth(nonce, APPROVE_MONTH_OFFSET)
  const reject = targetMonth(nonce, REJECT_MONTH_OFFSET)
  const approveLabel = `${DANISH_MONTHS[approve.month - 1]} ${approve.year}`
  const rejectLabel = `${DANISH_MONTHS[reject.month - 1]} ${reject.year}`
  // The two slots differ (9 ≢ 0 mod 18) ⇒ distinct months.
  expect(approveLabel).not.toBe(rejectLabel)

  // Candidate days per month (nonce-rotated mid-month weekdays).
  const approveDays = rotatedWeekdays(
    approve.year, approve.month, nonceWeekdayIndex(nonce, approve.year, approve.month, 0))
  const rejectDays = rotatedWeekdays(
    reject.year, reject.month, nonceWeekdayIndex(nonce, reject.year, reject.month, 0))

  // ── Setup: emp001 submits one SUBMITTED period in EACH target month ──
  await login(page, 'emp001')
  await submitPeriodViaForm(page, approveDays)
  await submitPeriodViaForm(page, rejectDays)
  await page.getByRole('button', { name: 'Log ud' }).click()
  await expect(page).toHaveURL(/\/login$/)

  // ── mgr03 drives the Teamoversigt (fresh login) ──
  await login(page, 'mgr03')
  await page.goto('/godkend/oversigt')
  await expect(page.getByRole('heading', { name: 'Teamoversigt' })).toBeVisible()

  const empRow: Locator = page.getByTestId('team-row-emp001')

  // ── APPROVE month: step onto it, the emp001 row is SUBMITTED ("Indsendt") ──
  await stepOversigtToMonth(page, approve.forwardClicks, approveLabel)
  await expect(empRow).toBeVisible()
  await expect(empRow.getByText('Indsendt')).toBeVisible()

  await empRow.getByRole('button', { name: 'Godkend' }).click()

  // DISCRIMINATING: the row's status badge transitions to "Godkendt" and the
  // SUBMITTED-only action buttons disappear (a true transition + refetch).
  await expect(empRow.getByText('Godkendt')).toBeVisible()
  await expect(empRow.getByRole('button', { name: 'Godkend' })).toHaveCount(0)
  await expect(empRow.getByRole('button', { name: 'Afvis' })).toHaveCount(0)

  // ── REJECT month: step onto it, the emp001 row is SUBMITTED ("Indsendt") ──
  // Step BACKWARD/FORWARD to the reject month. From the approve month we navigate
  // by reloading the page (re-init to current UTC month) then stepping forward.
  await page.goto('/godkend/oversigt')
  await expect(page.getByRole('heading', { name: 'Teamoversigt' })).toBeVisible()
  await stepOversigtToMonth(page, reject.forwardClicks, rejectLabel)
  await expect(empRow).toBeVisible()
  await expect(empRow.getByText('Indsendt')).toBeVisible()

  await empRow.getByRole('button', { name: 'Afvis' }).click()

  // The kit reject dialog opens; fill the optional reason and confirm.
  const reasonBox = page.getByPlaceholder('Skriv en kort begrundelse til medarbejderen…')
  await expect(reasonBox).toBeVisible()
  await reasonBox.fill('E2E afvisning — automatiseret journey 3')
  await page.getByRole('button', { name: 'Afvis måned' }).click()

  // DISCRIMINATING: the row's status badge transitions to "Afvist" and its
  // SUBMITTED-only action buttons are gone.
  await expect(empRow.getByText('Afvist')).toBeVisible()
  await expect(empRow.getByRole('button', { name: 'Godkend' })).toHaveCount(0)
  await expect(empRow.getByRole('button', { name: 'Afvis' })).toHaveCount(0)
})
