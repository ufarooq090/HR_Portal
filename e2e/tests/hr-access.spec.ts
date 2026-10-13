import { expect, test } from '@playwright/test'
import { devName, escapeRegExp, openNav, signInAs, switchUser } from './helpers'

// Uses the US HR users only, so the PK directory expectations in data-scope.spec.ts are unaffected.
test('MC-03: HR admin grants an HR executive a second country, then resets it', async ({ page }) => {
  test.slow() // four sign-ins in one journey
  const admin = devName('HrAdmin', 'US')
  const executive = devName('HrExecutive', 'US')

  // Default: a US HR executive sees the 9 US employees only.
  await signInAs(page, 'HrExecutive', 'US')
  await openNav(page, 'People')
  await expect(page.getByText('9 people', { exact: true })).toBeVisible()
  await switchUser(page, executive)

  await signInAs(page, 'HrAdmin', 'US')
  await openNav(page, 'HR data access')
  await page.getByRole('button', { name: 'Set access' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox', { name: 'Person' }).pressSequentially('HrExecutive (US)')
  await page.getByRole('option', { name: new RegExp(escapeRegExp(executive)) }).click()
  await dialog.getByRole('checkbox', { name: 'Pakistan (PK)' }).check()
  await dialog.getByRole('checkbox', { name: 'United States (US)' }).check()
  await dialog.getByRole('button', { name: 'Save' }).click()

  const row = page.getByRole('row', { name: new RegExp(escapeRegExp(executive)) })
  await expect(row).toContainText('PK, US')
  await switchUser(page, admin)

  await signInAs(page, 'HrExecutive', 'US')
  await openNav(page, 'People')
  await expect(page.getByText('18 people', { exact: true })).toBeVisible()
  await switchUser(page, executive)

  await signInAs(page, 'HrAdmin', 'US')
  await openNav(page, 'HR data access')
  await page.getByRole('button', { name: `Reset access for ${executive}` }).click()
  await expect(page.getByText(`${executive} is back to default access.`)).toBeVisible()
  await expect(row).toHaveCount(0)
})

test('MC-03: an HR admin cannot change their own access', async ({ page }) => {
  await signInAs(page, 'HrAdmin', 'US')
  await openNav(page, 'HR data access')
  await page.getByRole('button', { name: 'Set access' }).click()

  await page.getByRole('dialog').getByRole('combobox', { name: 'Person' }).pressSequentially('Dev Hr')

  // The picker lists other HR users but leaves the signed-in admin out; the API refuses it too (HrAccessEndpointTests).
  await expect(page.getByRole('option', { name: /Dev HrExecutive \(US\)/ })).toBeVisible()
  await expect(page.getByRole('option', { name: /Dev HrAdmin \(PK\)/ })).toBeVisible()
  await expect(page.getByRole('option', { name: /Dev HrAdmin \(US\)/ })).toHaveCount(0)
})
