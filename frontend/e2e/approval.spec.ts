import { test, expect, type Page, type Locator } from '@playwright/test'
import { login } from './helpers/auth'
import { runNonce, targetMonth, nonceWeekdayIndex, rotatedWeekdays } from './helpers/dates'

/**
 * SPRINT-82 journey 3 (R2c): the approval approve + reject chain.
 *
 * emp001 (AC / STY01) submits TWO raw SUBMITTED periods via the MyPeriods submit
 * form (POST /api/approval/submit — a raw SUBMITTED period with NO Skema-content
 * dependency; NOT Skema's submitAndApprove, which employee-approves + requires full
 * coverage). Then mgr03 — the seeded emp001→mgr03 STY01 designated approver — drives
 * the ApprovalDashboard: APPROVES the first period (SUBMITTED → APPROVED) and REJECTS
 * the second (SUBMITTED → REJECTED via the reject dialog). Two SEPARATE periods
 * because approve/reject are both terminal.
 *
 * Re-run tolerance: the per-run nonce targets a UNIQUE future month and a nonce-rotated
 * starting day; the resilient submit then walks the month's mid-month weekdays until it
 * finds two FREE days (stepping over any SUBMITTED/APPROVED/REJECTED period a prior run
 * left on a recurring slot in the persistent local volume — the /submit guard refuses
 * re-submit, ApprovalEndpoints.cs:109). CI's fresh ephemeral volume always takes the
 * first two candidates. The approve + reject periods are always two distinct days.
 *
 * Assertions are DISCRIMINATING: each row is located by a stable data-testid keyed on
 * its ISO periodStart; the test asserts the row's STATUS BADGE transitioned
 * (Indsendt → Godkendt / Afvist) AND the SUBMITTED-only action buttons are gone — not
 * merely that a button was clickable.
 */

const DANISH_MONTHS = [
  'januar', 'februar', 'marts', 'april', 'maj', 'juni',
  'juli', 'august', 'september', 'oktober', 'november', 'december',
]

// Per-SPEC month offset: this journey + the skema-registration journey both mutate
// emp001 and run in PARALLEL. The offset (skema uses 0, approval uses 7) is a
// best-effort spread, NOT a collision guarantee: each spec calls runNonce()
// independently (its own per-spec wall-clock nonce), so the two months are NOT
// guaranteed to differ — `(nonceSkema+0)` and `(nonceApproval+7)` can still land on
// the same slot mod 18.
//
// Why same-month is nonetheless BENIGN (the real reason): the two journeys never
// collide on the period-lock 409 because they create DIFFERENT-SHAPED periods. The
// approval journey submits SINGLE-DAY periods (periodStart == periodEnd on a chosen
// weekday), whereas the skema period-lock keys on the EXACT full registered period
// (SkemaEndpoints.cs:604 rejects a save only when its month is locked by an
// APPROVED period covering that exact period span). A single-day approval period and
// skema's mid-month single-day SICK_DAY registration land on distinct days anyway,
// so neither the /submit existing-period guard nor the skema period-lock can fire
// across the two specs even when they share a month.
const APPROVAL_MONTH_OFFSET = 7

/**
 * Submit a raw SUBMITTED single-day period for the logged-in emp001 via the
 * MyPeriods form, walking the ordered `candidateDays` until one yields a clean
 * SUBMITTED period; returns the day actually used (its data-testid handle).
 *
 * Resilience: on the persistent LOCAL volume a candidate day may already hold an
 * APPROVED/REJECTED/SUBMITTED period from a prior run → /submit 409s ("Period
 * already exists ..."). We simply STEP to the next candidate (≤ 9 in a month).
 * On CI's fresh ephemeral volume the first candidate always wins. `excludeDays`
 * skips days already claimed earlier in THIS run (the approve target).
 */
async function submitPeriodViaForm(
  page: Page,
  candidateDays: string[],
  excludeDays: string[] = [],
): Promise<string> {
  await page.goto('/tid/mine-perioder')
  await expect(page.getByRole('heading', { name: 'Mine perioder' })).toBeVisible()

  for (const isoDay of candidateDays) {
    if (excludeDays.includes(isoDay)) continue

    // The date inputs carry real <label htmlFor> associations (Startdato / Slutdato).
    // type=date inputs accept the ISO YYYY-MM-DD value via fill().
    await page.getByLabel('Startdato').fill(isoDay)
    await page.getByLabel('Slutdato').fill(isoDay)
    // Periodetype + Overenskomst default to WEEKLY / AC (valid for emp001).

    // Capture the submit response regardless of status, then branch on it.
    const submitResp = page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/approval/submit') && resp.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Indsend periode' }).click()
    const resp = await submitResp

    if (resp.status() === 200) {
      // The form surfaces the success banner on a 200 — a real, server-confirmed submit.
      await expect(page.getByText('Periode indsendt.')).toBeVisible()
      return isoDay
    }
    // 409 ("Period already exists ...") on a recurring local slot → try the next day.
    // The form stays put (it only sets a form-error), so the next iteration re-fills.
  }
  throw new Error(
    `submitPeriodViaForm: no free candidate day among ${candidateDays.join(', ')} (excluding ${excludeDays.join(', ')})`,
  )
}

/** Drive the ApprovalDashboard month-nav (internal year/month state, no URL param)
 *  FORWARD `clicks` whole months from the current UTC month onto the target. */
async function navigateDashboardToMonth(page: Page, clicks: number, monthLabel: string): Promise<void> {
  // The dashboard's "next" control reads "Naeste →" (ASCII ae, not æ).
  const next = page.getByRole('button', { name: /Naeste/ })
  for (let i = 0; i < clicks; i++) {
    await next.click()
  }
  await expect(page.getByRole('heading', { name: monthLabel })).toBeVisible()
}

test('mgr03 approves one emp001 period and rejects another from the dashboard', async ({ page }) => {
  const nonce = runNonce()
  const { year, month, forwardClicks } = targetMonth(nonce, APPROVAL_MONTH_OFFSET)
  const monthLabel = `${DANISH_MONTHS[month - 1]} ${year}`

  // Two distinct non-boundary weekdays in the SAME nonce month → two isolated periods.
  // We submit on the FIRST FREE day from a nonce-rotated candidate list (≥ 9 mid-month
  // weekdays): the per-run nonce spreads runs across the (month × day) slot space, and
  // the resilient submit steps over any slot a PRIOR run already claimed on the local
  // persistent volume (the /submit guard refuses re-submit over SUBMITTED/APPROVED —
  // ApprovalEndpoints.cs:109). The reject submit excludes the approve day, so the two
  // periods are always distinct. CI's fresh volume always takes the first candidate.
  const startIdx = nonceWeekdayIndex(nonce, year, month, 0)
  const candidateDays = rotatedWeekdays(year, month, startIdx)

  // ── Setup: emp001 submits the two raw SUBMITTED periods ──
  await login(page, 'emp001')
  const approveDay = await submitPeriodViaForm(page, candidateDays)
  const rejectDay = await submitPeriodViaForm(page, candidateDays, [approveDay])
  expect(approveDay).not.toBe(rejectDay)
  await page.getByRole('button', { name: 'Log ud' }).click()
  await expect(page).toHaveURL(/\/login$/)

  // ── mgr03 drives the ApprovalDashboard (fresh login) ──
  await login(page, 'mgr03')
  await page.goto('/godkend/godkendelser')
  await navigateDashboardToMonth(page, forwardClicks, monthLabel)

  const approveRow: Locator = page.getByTestId(`period-row-${approveDay}`)
  const rejectRow: Locator = page.getByTestId(`period-row-${rejectDay}`)

  // Both periods are visible in the default "Mine medarbejdere" tab (emp001 is mgr03's
  // report) and both start SUBMITTED ("Indsendt").
  await expect(approveRow).toBeVisible()
  await expect(rejectRow).toBeVisible()
  await expect(approveRow.getByText('Indsendt')).toBeVisible()
  await expect(rejectRow.getByText('Indsendt')).toBeVisible()

  // ── APPROVE the first period ──
  await approveRow.getByRole('button', { name: 'Godkend' }).click()

  // DISCRIMINATING: the row's status badge transitions to "Godkendt" and the
  // SUBMITTED-only action buttons disappear (the period truly transitioned, not just
  // a clickable button). Web-first auto-wait absorbs the refetch.
  await expect(approveRow.getByText('Godkendt')).toBeVisible()
  await expect(approveRow.getByRole('button', { name: 'Godkend' })).toHaveCount(0)
  await expect(approveRow.getByRole('button', { name: 'Afvis' })).toHaveCount(0)

  // The reject-target is still SUBMITTED and untouched.
  await expect(rejectRow.getByText('Indsendt')).toBeVisible()

  // ── REJECT the second period via the reject dialog ──
  await rejectRow.getByRole('button', { name: 'Afvis' }).click()

  // The rejection dialog opens; fill the reason (the confirm button is trim()-gated).
  const reasonBox = page.getByPlaceholder('Begrundelse for afvisning...')
  await expect(reasonBox).toBeVisible()
  await reasonBox.fill('E2E afvisning — automatiseret journey 3')
  await page.getByRole('button', { name: 'Afvis periode' }).click()

  // DISCRIMINATING: the second row's status badge transitions to "Afvist" and its
  // SUBMITTED-only action buttons are gone.
  await expect(rejectRow.getByText('Afvist')).toBeVisible()
  await expect(rejectRow.getByRole('button', { name: 'Godkend' })).toHaveCount(0)
  await expect(rejectRow.getByRole('button', { name: 'Afvis' })).toHaveCount(0)

  // The approve-target remains APPROVED (no cross-contamination).
  await expect(approveRow.getByText('Godkendt')).toBeVisible()
})
