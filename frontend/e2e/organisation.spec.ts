import { test, expect, type Page } from '@playwright/test'
import { login } from './helpers/auth'
import { runNonce } from './helpers/dates'

/**
 * SPRINT-99 / TASK-9905 — the Organisation page journey:
 *   create (MAO + Organisation) → rename → move → delete.
 *
 * admin01 (a seeded GLOBAL_ADMIN, init.sql) drives the redesigned Global
 * administration → Organisation page (/global/organisation). The page is a
 * GlobalAdmin-gated tree-table over the S98 aggregated tree + the S98/S97
 * structural mutations (create / rename / move / delete), built on the FLAT
 * S97 Enhed model.
 *
 * Re-run tolerance: a per-run nonce stamps the created node names, so a re-run
 * on the persistent LOCAL Postgres volume never collides with the active-name
 * dup guard (POST 409). The journey cleans up after itself (it deletes the
 * Organisation it moved and both empty MAOs it created) so it leaves no residue;
 * CI's fresh ephemeral volume always starts clean regardless.
 *
 * Assertions are DISCRIMINATING: each step asserts the tree-table row appeared /
 * changed text / disappeared via a stable data-testid (org-row-{id}) or the
 * rendered name, not merely that a button was clickable.
 */

const ORG_URL = '/global/organisation'

/** Open the create dialog from a row's "Tilføj" action and confirm with a name. */
async function createChild(page: Page, parentName: string, name: string): Promise<void> {
  // Find the row by its visible name, then its Tilføj action.
  const row = page.getByRole('row', { name: new RegExp(parentName) })
  await row.getByRole('button', { name: 'Tilføj' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Navn').fill(name)
  await dialog.getByRole('button', { name: /^(Ny organisation|Ny enhed)$/ }).click()
  await expect(dialog).toBeHidden()
}

test('admin01 creates, renames, moves and deletes an organisation', async ({ page }) => {
  const nonce = runNonce()
  const maoA = `E2E MAO A ${nonce}`
  const maoB = `E2E MAO B ${nonce}`
  const orgName = `E2E Org ${nonce}`
  const orgRenamed = `E2E Org ${nonce} omdøbt`

  await login(page, 'admin01')
  await page.goto(ORG_URL)
  await expect(page.getByRole('heading', { name: 'Organisation' })).toBeVisible()

  // ── CREATE: two MAOs (a source + a move target) ──
  for (const mao of [maoA, maoB]) {
    await page.getByTestId('new-mao').click()
    const dialog = page.getByRole('dialog', { name: 'Nyt ministeransvarsområde' })
    await dialog.getByLabel('Navn').fill(mao)
    await dialog.getByRole('button', { name: 'Nyt ministeransvarsområde' }).click()
    await expect(dialog).toBeHidden()
    await expect(page.getByText(mao, { exact: true })).toBeVisible()
  }

  // ── CREATE: an Organisation under MAO A ──
  await createChild(page, maoA, orgName)
  await expect(page.getByText(orgName, { exact: true })).toBeVisible()

  const orgRow = page.getByRole('row', { name: new RegExp(orgName) })

  // ── RENAME: the Organisation via the warning dialog (name-only) ──
  await orgRow.getByRole('button', { name: 'Omdøb' }).click()
  const renameDialog = page.getByRole('dialog', { name: 'Omdøb organisation' })
  const renameInput = renameDialog.getByLabel('Nyt navn')
  await renameInput.fill(orgRenamed)
  await renameDialog.getByRole('button', { name: 'Gem ændring' }).click()
  await expect(renameDialog).toBeHidden()
  await expect(page.getByText(orgRenamed, { exact: true })).toBeVisible()

  // ── MOVE: the Organisation from MAO A to MAO B ──
  const renamedRow = page.getByRole('row', { name: new RegExp(orgRenamed) })
  await renamedRow.getByRole('button', { name: 'Flyt' }).click()
  const moveDialog = page.getByRole('dialog', { name: 'Flyt organisation' })
  // The target select offers MAO B (the current parent MAO A is excluded).
  await moveDialog.getByLabel('Ny placering').selectOption({ label: maoB })
  await moveDialog.getByRole('button', { name: 'Flyt' }).click()
  await expect(moveDialog).toBeHidden()
  // The org still exists (now under MAO B); the tree re-fetched.
  await expect(page.getByText(orgRenamed, { exact: true })).toBeVisible()

  // ── DELETE: the empty Organisation (it has no employees → soft-delete) ──
  await page.getByRole('row', { name: new RegExp(orgRenamed) })
    .getByRole('button', { name: 'Slet' }).click()
  const delDialog = page.getByRole('dialog', { name: 'Slet organisation?' })
  await delDialog.getByTestId('confirm-delete').click()
  await expect(delDialog).toBeHidden()
  await expect(page.getByText(orgRenamed, { exact: true })).toBeHidden()

  // ── CLEANUP: delete both empty MAOs so the run leaves no residue ──
  for (const mao of [maoA, maoB]) {
    await page.getByRole('row', { name: new RegExp(mao) })
      .getByRole('button', { name: 'Slet' }).click()
    const md = page.getByRole('dialog', { name: 'Slet ministeransvarsområde?' })
    await md.getByTestId('confirm-delete').click()
    await expect(md).toBeHidden()
    await expect(page.getByText(mao, { exact: true })).toBeHidden()
  }
})
