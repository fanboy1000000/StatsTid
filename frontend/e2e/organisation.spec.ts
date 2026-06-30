import { test, expect, type Page } from '@playwright/test'
import { login } from './helpers/auth'
import { runNonce } from './helpers/dates'

/**
 * SPRINT-109 / TASK-10905 — the merged "Organisation & medarbejdere" admin page.
 *
 * REPLACES the retired SPRINT-99 `/global/organisation` journey: the cutover
 * (TASK-10904) retired MedarbejderAdministration.tsx + OrganisationPage.tsx and
 * redirected both old routes to the single merged surface at
 * `/admin/organisation-medarbejdere`. The old spec's DOM (the GlobalAdmin
 * tree-table + its create/rename/move/delete dialogs) is gone, so this spec is
 * rewritten end-to-end against the merged page's real DOM/testids.
 *
 * admin01 (a seeded GLOBAL_ADMIN, init.sql) drives the page — GlobalAdmin sees the
 * whole forest and clears every capability floor (LocalHR people-edit + unit
 * structure). The merged page lives behind the LocalHR route gate (App.tsx).
 *
 * Two happy paths, each a real backend round-trip the vitest tier (mocked hooks)
 * cannot exercise:
 *
 *   1. PEOPLE-EDIT (the S109 keystone): select the Organisation Statens IT (STY02)
 *      → expand its unit tree → open a member's "Rediger ›" drawer → change the
 *      display name → save → assert the PUT round-trips AND the roster row reflects
 *      the new name (the merged page refetched). The seeded STY02 tree has the
 *      IT-Drift kontor (mgr01 leads it; emp002 is a member — init.sql units +
 *      reporting_lines), so emp002 always renders a "Rediger ›" affordance.
 *
 *   2. STRUCTURE (create → rename → delete a unit): on the STY02 Organisation node
 *      create a top-level unit, select it, rename it, then delete it. The delete is
 *      self-cleanup so the run leaves no residue (mirroring the old spec's
 *      discipline); CI's fresh ephemeral volume always starts clean regardless.
 *
 * Re-run tolerance: a per-run nonce stamps every name written (the edited person's
 * new display name + the created unit's name), so a re-run on the persistent LOCAL
 * Postgres volume never collides with the active-name unique index (units) and the
 * edit assertion is always discriminating (a fresh, unique value that must appear).
 *
 * Assertions are DISCRIMINATING: the person row is located by its stable
 * employeeId-keyed testid (person-edit-emp002) and the test asserts the rendered
 * NAME changed after the save round-trip (not merely that a button was clickable);
 * the structure path asserts the tree row appeared / disappeared by its rendered
 * name.
 */

const MERGED_URL = '/admin/organisation-medarbejdere'
const STY02 = 'STY02' // Statens IT — the seeded Organisation carrying the unit tree.

/** Open the merged page (post-login) and wait for the org-structure tree to render. */
async function openMergedPage(page: Page): Promise<void> {
  await page.goto(MERGED_URL)
  await expect(page.getByText('Organisation & medarbejdere')).toBeVisible()
  await expect(page.getByTestId('org-structure-tree')).toBeVisible()
}

test('admin01 edits a person from the merged Organisation & medarbejdere page', async ({ page }) => {
  const nonce = runNonce()
  const newName = `Karen Nielsen ${nonce}`

  await login(page, 'admin01')
  await openMergedPage(page)

  // ── Select the Organisation Statens IT (STY02) in the left tree ──
  await page.getByTestId('tree-row-STY02').click()
  await expect(page.getByTestId('title-name')).toHaveText('Statens IT')

  // Expand the whole unit sub-tree so the IT-Drift members (mgr01 + emp002) render.
  await page.getByTestId('toggle-expand-all').click()

  // ── Open emp002 (Karen Nielsen)'s edit drawer via its "Rediger ›" affordance ──
  const editBtn = page.getByTestId('person-edit-emp002')
  await expect(editBtn).toBeVisible()
  await editBtn.click()

  // The drawer opens in edit mode and hydrates (host fetchUser + the drawer's own
  // profile/entitlement fetch). Wait for the loading state to clear so the form is
  // interactive (the inputs + submit are disabled while busy).
  await expect(page.getByTestId('person-drawer-title')).toBeVisible()
  await expect(page.getByTestId('person-drawer-loading')).toBeHidden()

  // ── Change the display name to a unique, nonce-stamped value ──
  const nameInput = page.getByTestId('ep-display-name')
  await expect(nameInput).toHaveValue(/.+/) // hydrated with the current name
  await nameInput.fill(newName)

  // ── Save — the stamdata PUT round-trips to /api/admin/users/emp002 ──
  const putResp = page.waitForResponse(
    (resp) =>
      resp.url().includes('/api/admin/users/emp002') && resp.request().method() === 'PUT',
  )
  await page.getByRole('button', { name: 'Gem ændringer' }).click()
  await putResp

  // DISCRIMINATING: the drawer closes on success AND the refetched roster row shows
  // the new name (the edit truly persisted + the merged page re-pulled the roster).
  await expect(page.getByTestId('person-drawer-title')).toBeHidden()
  await expect(page.getByTestId('person-edit-emp002')).toBeVisible()
  await expect(page.getByText(newName, { exact: true })).toBeVisible()
})

test('admin01 creates, renames and deletes a unit on the merged page', async ({ page }) => {
  const nonce = runNonce()
  const unitName = `E2E Enhed ${nonce}`
  const unitRenamed = `E2E Enhed ${nonce} omdøbt`

  await login(page, 'admin01')
  await openMergedPage(page)

  // ── Select the Organisation STY02 → its action row hosts "+ Direktion" ──
  await page.getByTestId('tree-row-STY02').click()
  await expect(page.getByTestId('title-name')).toHaveText('Statens IT')

  // ── CREATE: a top-level unit under STY02 ──
  await page.getByTestId('unit-action-create').click()
  const createDrawer = page.getByRole('dialog')
  await expect(page.getByTestId('unit-drawer-title')).toBeVisible()
  await createDrawer.getByTestId('unit-drawer-name').fill(unitName)
  await createDrawer.getByTestId('unit-drawer-submit').click()
  await expect(page.getByTestId('unit-drawer-title')).toBeHidden()
  // The forest refetched → the new unit appears in the left tree under STY02.
  await expect(page.getByText(unitName, { exact: true })).toBeVisible()

  // ── SELECT the new unit (left tree row, located by its unique name) ──
  await page.getByRole('treeitem').filter({ hasText: unitName }).click()
  await expect(page.getByTestId('title-name')).toHaveText(unitName)

  // ── RENAME: via the unit "Rediger" action ──
  await page.getByTestId('unit-action-edit').click()
  const editDrawer = page.getByRole('dialog')
  await expect(page.getByTestId('unit-drawer-title')).toBeVisible()
  const renameInput = editDrawer.getByTestId('unit-drawer-name')
  await renameInput.fill(unitRenamed)
  await editDrawer.getByTestId('unit-drawer-submit').click()
  await expect(page.getByTestId('unit-drawer-title')).toBeHidden()
  await expect(page.getByTestId('title-name')).toHaveText(unitRenamed)

  // ── DELETE: the renamed unit (self-cleanup → the run leaves no residue) ──
  await page.getByTestId('unit-action-delete').click()
  await expect(page.getByTestId('unit-delete-warning')).toBeVisible()
  await page.getByTestId('unit-delete-confirm').click()
  await expect(page.getByTestId('unit-delete-warning')).toBeHidden()
  // The unit is gone from the tree (no row carries either name any longer).
  await expect(page.getByText(unitName, { exact: true })).toBeHidden()
  await expect(page.getByText(unitRenamed, { exact: true })).toBeHidden()
})
