import { expect, test } from '@playwright/test'

// Mirrors HrPortal.Domain.Security.Roles and the seeded users in DevUsers (one per role in each country).
const roles = ['SystemAdmin', 'HrAdmin', 'HrExecutive', 'HiringManager', 'Interviewer', 'LineManager', 'Employee']
const countries = ['PK', 'US']
const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5295'

for (const country of countries) {
  for (const role of roles) {
    const displayName = `Dev ${role} (${country})`

    test(`NFR-01 smoke: signs in via DevAuth as ${role} (${country})`, async ({ page }) => {
      await page.goto('/')
      await page.getByRole('button', { name: `Sign in as ${displayName}` }).click()

      // The welcome heading and role badges come from /api/me, so the API accepted the DevAuth identity.
      await expect(page.getByRole('heading', { level: 1, name: `Welcome, ${displayName}` })).toBeVisible()
      const main = page.getByRole('main')
      const expectedRoles = role === 'Employee' ? ['Employee'] : [role, 'Employee']
      for (const expected of expectedRoles) {
        await expect(main.getByText(expected, { exact: true })).toBeVisible()
      }

      await page.getByRole('button', { name: displayName }).click()
      await page.getByRole('menuitem', { name: 'Switch user' }).click()
      await expect(page.getByRole('heading', { name: 'HR Portal — development sign-in' })).toBeVisible()
    })
  }
}

test('NFR-02 smoke: API rejects requests without a signed-in user', async ({ request }) => {
  const anonymous = await request.get(`${apiUrl}/api/me`)
  expect(anonymous.status()).toBe(401)

  const signedIn = await request.get(`${apiUrl}/api/me`, { headers: { 'X-Dev-User': 'dev-pk-employee' } })
  expect(signedIn.status()).toBe(200)
})
