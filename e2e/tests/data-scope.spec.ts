import { expect, test } from '@playwright/test'
import { apiUrl, devId, devName, openNav, rowFor, signInAs } from './helpers'

// Seeded by DevDataSeeder: reporting lines are HrAdmin -> HrExecutive; HiringManager -> {LineManager, Interviewer};
// LineManager -> Employee -> Junior Engineer; plus a Finance Analyst nobody manages. HR Admins see both countries.
const pk = {
  hrAdmin: devName('HrAdmin', 'PK'),
  hrExecutive: devName('HrExecutive', 'PK'),
  systemAdmin: devName('SystemAdmin', 'PK'),
  hiringManager: devName('HiringManager', 'PK'),
  lineManager: devName('LineManager', 'PK'),
  employee: devName('Employee', 'PK'),
  interviewer: devName('Interviewer', 'PK'),
  junior: 'Junior Engineer (PK)',
  finance: 'Finance Analyst (PK)',
}
const everyonePk = Object.values(pk)

const expectations: { role: string; count: number; sees: string[] }[] = [
  { role: 'HrAdmin', count: 18, sees: [...everyonePk, devName('Employee', 'US')] },
  { role: 'HrExecutive', count: 9, sees: everyonePk },
  { role: 'HiringManager', count: 5, sees: [pk.hiringManager, pk.lineManager, pk.interviewer, pk.employee, pk.junior] },
  { role: 'LineManager', count: 3, sees: [pk.lineManager, pk.employee, pk.junior] },
  { role: 'Interviewer', count: 1, sees: [pk.interviewer] },
  { role: 'Employee', count: 1, sees: [pk.employee] },
  { role: 'SystemAdmin', count: 1, sees: [pk.systemAdmin] },
]

for (const { role, count, sees } of expectations) {
  test(`NFR-02 / MC-03: ${role} (PK) sees only their scope in the People directory`, async ({ page }) => {
    await signInAs(page, role, 'PK')
    await openNav(page, 'People')

    const table = page.getByRole('table', { name: 'People' })
    await expect(page.getByText(count === 1 ? '1 person' : `${count} people`, { exact: true })).toBeVisible()
    await expect(table.getByRole('row')).toHaveCount(count + 1)
    for (const name of sees) {
      await expect(rowFor(table, name)).toHaveCount(1)
    }
    for (const name of everyonePk.filter((n) => !sees.includes(n))) {
      await expect(rowFor(table, name)).toHaveCount(0)
    }
  })
}

test('NFR-02: search never reaches beyond the scope', async ({ page }) => {
  await signInAs(page, 'LineManager', 'PK')
  await openNav(page, 'People')

  await page.getByRole('searchbox', { name: 'Search people' }).fill('Finance')

  await expect(page.getByText('0 people', { exact: true })).toBeVisible()
})

test('NFR-02: the API refuses an out-of-scope employee record with 403', async ({ request }) => {
  const all = await request.get(`${apiUrl}/api/employees?search=PK-0009`, { headers: { 'X-Dev-User': devId('HrAdmin', 'PK') } })
  expect(all.status()).toBe(200)
  const [finance] = (await all.json()) as { id: string }[]

  const asManager = await request.get(`${apiUrl}/api/employees/${finance!.id}`, {
    headers: { 'X-Dev-User': devId('LineManager', 'PK') },
  })
  const asHr = await request.get(`${apiUrl}/api/employees/${finance!.id}`, {
    headers: { 'X-Dev-User': devId('HrExecutive', 'PK') },
  })

  expect(asManager.status()).toBe(403)
  expect(asHr.status()).toBe(200)
})
