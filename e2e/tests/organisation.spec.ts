import { expect, type Page, test } from '@playwright/test'
import { apiUrl, devId, openNav, signInAs, unique } from './helpers'

async function chooseCountry(page: Page, option: string) {
  await page.getByRole('dialog').getByRole('combobox', { name: 'Country' }).click()
  await page.getByRole('option', { name: option }).click()
}

test('MC-01: HR admin adds, deactivates and deletes a department', async ({ page }) => {
  const code = `Q${unique()}`
  await signInAs(page, 'HrAdmin', 'PK')
  await openNav(page, 'Departments')

  await page.getByRole('button', { name: 'Add department' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Code').fill(code.toLowerCase())
  await dialog.getByLabel('Name').fill(`Quality ${code}`)
  await dialog.getByRole('button', { name: 'Save' }).click()

  const row = page.getByRole('row', { name: new RegExp(code) })
  await expect(row).toContainText(code)
  await expect(row).toContainText('Active')

  await page.getByRole('button', { name: `Edit Quality ${code}` }).click()
  await page.getByRole('dialog').getByRole('switch', { name: 'Active' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(row).toContainText('Inactive')

  await page.getByRole('button', { name: `Delete Quality ${code}` }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click()
  await expect(row).toHaveCount(0)
})

test('MC-01: departments in use cannot be deleted', async ({ page }) => {
  await signInAs(page, 'HrAdmin', 'PK')
  await openNav(page, 'Departments')

  await page.getByRole('button', { name: 'Delete Engineering' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click()

  await expect(page.getByRole('dialog')).toContainText('in use')
})

test('MC-01: legal entity and office are created in a country; the office inherits its time zone', async ({ page }) => {
  const suffix = unique()
  await signInAs(page, 'HrAdmin', 'PK')

  await openNav(page, 'Legal entities')
  await page.getByRole('button', { name: 'Add legal entity' }).click()
  await chooseCountry(page, 'Pakistan (PK)')
  await page.getByRole('dialog').getByLabel('Name').fill(`Test Entity ${suffix} (Pvt) Ltd`)
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('row', { name: new RegExp(`Test Entity ${suffix}`) })).toContainText('PK')

  await openNav(page, 'Offices')
  await page.getByRole('button', { name: 'Add office' }).click()
  await chooseCountry(page, 'Pakistan (PK)')
  await page.getByRole('dialog').getByLabel('Name').fill(`Islamabad ${suffix}`)
  await page.getByRole('dialog').getByLabel('City').fill('Islamabad')
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('row', { name: new RegExp(`Islamabad ${suffix}`) })).toContainText('Asia/Karachi (country default)')
})

test('MC-01: a grade can override the country notice period', async ({ page }) => {
  const code = `X${unique()}`
  await signInAs(page, 'HrAdmin', 'US')
  await openNav(page, 'Grades')

  await page.getByRole('button', { name: 'Add grade' }).click()
  await chooseCountry(page, 'United States (US)')
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Code').fill(code)
  await dialog.getByLabel('Name').fill('Executive')
  await dialog.getByLabel('Level').fill('8')
  await dialog.getByLabel('Notice period override (days)').fill('90')
  await dialog.getByRole('button', { name: 'Save' }).click()

  const row = page.getByRole('row', { name: new RegExp(code) })
  await expect(row).toContainText('90 days')
  await expect(row).toContainText('months (country default)')
})

test('MC-01: validation errors from the API are shown on the field', async ({ page }) => {
  await signInAs(page, 'HrAdmin', 'PK')
  await openNav(page, 'Offices')

  await page.getByRole('button', { name: 'Add office' }).click()
  await chooseCountry(page, 'Pakistan (PK)')
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Name').fill(`Nowhere ${unique()}`)
  await dialog.getByLabel('Time zone override').fill('Lahore')
  await dialog.getByRole('button', { name: 'Save' }).click()

  await expect(dialog).toContainText('Must be an IANA time zone')
})

test('NFR-02: HR executive can read organisation settings but not change them', async ({ page, request }) => {
  await signInAs(page, 'HrExecutive', 'PK')
  await openNav(page, 'Departments')

  await expect(page.getByRole('table', { name: 'Departments' })).toContainText('Engineering')
  await expect(page.getByRole('button', { name: 'Add department' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /^Edit / })).toHaveCount(0)
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'HR data access' })).toHaveCount(0)

  const attempt = await request.post(`${apiUrl}/api/organisation/departments`, {
    headers: { 'X-Dev-User': devId('HrExecutive', 'PK') },
    data: { code: `NOPE${unique()}`, name: 'Not allowed' },
  })
  expect(attempt.status()).toBe(403)
})

test('NFR-02: employees and managers do not get the settings section', async ({ page }) => {
  await signInAs(page, 'LineManager', 'US')

  const nav = page.getByRole('navigation', { name: 'Main' })
  await expect(nav.getByRole('link', { name: 'People' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Departments' })).toHaveCount(0)
})
