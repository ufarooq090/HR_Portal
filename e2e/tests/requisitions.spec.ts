import { type APIRequestContext, expect, type Page, test } from '@playwright/test'
import { apiUrl, asUser, devName, openNav, signInAs, switchUser, unique } from './helpers'

const mailpitUrl = process.env.E2E_MAILPIT_URL ?? 'http://127.0.0.1:8025'

async function choose(page: Page, field: string, option: string) {
  await page.getByRole('combobox', { name: field }).click()
  await page.getByRole('option', { name: option }).click()
}

/** The CEO is the seeded default approver in both countries. */
async function signInAsCeo(page: Page) {
  await page.goto('/')
  await page.getByRole('button', { name: 'Sign in as Dev CEO' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome, Dev CEO' })).toBeVisible()
}

/** Raises (and optionally submits) a requisition through the API, as a role in Pakistan. */
async function raise(request: APIRequestContext, role: string, department: 'ENG' | 'FIN', submit: boolean) {
  const headers = asUser(role, 'PK')
  const get = async <T>(path: string) => (await (await request.get(`${apiUrl}${path}`, { headers })).json()) as T
  const departments = await get<{ id: string; code: string }[]>('/api/organisation/departments')
  const offices = await get<{ id: string; name: string }[]>('/api/organisation/locations')
  const title = `E2E Analyst ${unique()}`
  const created = await request.post(`${apiUrl}/api/requisitions`, {
    headers,
    data: {
      title,
      departmentId: departments.find((d) => d.code === department)!.id,
      officeLocationId: offices.find((o) => o.name === 'Lahore Office')!.id,
      openings: 1,
      budgetMin: 1_500_000,
      budgetMax: 2_000_000,
      reason: 'NewPosition',
      replacedEmployeeId: null,
      targetDate: '2030-06-30',
      jobDescription: 'Analyse things.',
    },
  })
  expect(created.status()).toBe(201)
  const requisition = (await created.json()) as { id: string; number: string }
  if (submit) expect((await request.post(`${apiUrl}/api/requisitions/${requisition.id}/submit`, { headers, data: {} })).status()).toBe(200)
  return { ...requisition, title }
}

/** Emails Mailpit has received for a requisition, as "recipient: subject". */
async function emailsAbout(request: APIRequestContext, number: string): Promise<string[]> {
  const response = await request.get(`${mailpitUrl}/api/v1/search?query=${encodeURIComponent(`subject:"${number}"`)}`)
  const body = (await response.json()) as { messages: { To: { Address: string }[]; Subject: string }[] }
  return body.messages.flatMap((m) => m.To.map((t) => `${t.Address}: ${m.Subject}`))
}

test('REQ-01..03: manager raises and submits, the CEO approves, the requester is notified in-app and by email', async ({ page, request }) => {
  test.slow()
  const role = `Platform Engineer ${unique()}`

  await signInAs(page, 'LineManager', 'PK')
  await openNav(page, 'Requisitions')
  await page.getByRole('button', { name: 'Raise requisition' }).click()
  await page.getByLabel('Role').fill(role)
  await choose(page, 'Department', 'Engineering')
  await choose(page, 'Office', 'Lahore Office (PK)')
  await page.getByLabel('Openings').fill('2')
  await page.getByLabel('Budget from (PKR)').fill('2,400,000')
  await page.getByLabel('Budget to (PKR)').fill('3,600,000')
  await page.getByLabel('Target date').fill('2030-03-31')
  await page.getByLabel('Job description').fill('Own our deployment platform.')
  await page.getByRole('button', { name: 'Save draft' }).click()

  const heading = page.getByRole('heading', { level: 1, name: new RegExp(`REQ-\\d{5} ${role}`) })
  await expect(heading).toBeVisible()
  const number = (await heading.textContent())!.split(' ')[0]!
  await expect(page.getByText('PKR 2,400,000 – 3,600,000')).toBeVisible()
  await page.getByRole('button', { name: 'Submit for approval' }).click()
  await expect(page.getByText('Pending approval', { exact: true })).toBeVisible()
  await switchUser(page, devName('LineManager', 'PK'))

  await signInAsCeo(page)
  await openNav(page, 'Approvals')
  await page.getByRole('link', { name: `${number} ${role}` }).click()
  await page.getByRole('button', { name: 'Approve' }).click()
  await page.getByRole('dialog').getByLabel('Comment').fill('Approved for Q1.')
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click()
  await expect(page.getByText('Open', { exact: true }).first()).toBeVisible()
  await switchUser(page, 'Dev CEO')

  await signInAs(page, 'LineManager', 'PK')
  await page.getByRole('button', { name: /Notifications, \d+ unread/ }).click()
  await page.getByRole('button', { name: new RegExp(`Requisition approved: ${number}`) }).click()
  await expect(page.getByRole('heading', { level: 1, name: `${number} ${role}` })).toBeVisible()
  await expect(page.getByRole('table', { name: 'Approvals' })).toContainText('Approved for Q1.')

  // Both emails went out through Mailpit (sent by the background job).
  await expect
    .poll(() => emailsAbout(request, number), { timeout: 30_000 })
    .toEqual(
      expect.arrayContaining([
        expect.stringMatching(/^dev-ceo@hrportal\.test: Approval needed/),
        expect.stringMatching(/^dev-pk-linemanager@hrportal\.test: Requisition approved/),
      ]),
    )
})

test('REQ-02: rejection needs a reason and sends the requisition back to draft', async ({ page, request }) => {
  const requisition = await raise(request, 'HiringManager', 'ENG', true)

  await signInAsCeo(page)
  await page.goto(`/requisitions/${requisition.id}`)
  await page.getByRole('button', { name: 'Reject' }).click()
  // The comment is required: an empty rejection is not sent (the API refuses it too).
  await expect(page.getByRole('dialog').getByLabel('Comment')).toHaveAttribute('required', '')
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByRole('dialog').getByLabel('Comment').fill('Combine with the platform role.')
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click()

  await expect(page.getByText('Draft', { exact: true }).first()).toBeVisible()
  await expect(page.getByRole('table', { name: 'Status history' })).toContainText('Combine with the platform role.')
  await expect(page.getByRole('button', { name: 'Approve' })).toHaveCount(0)
})

test('REQ-04: HR puts an open position on hold and reopens it', async ({ page, request }) => {
  const requisition = await raise(request, 'LineManager', 'ENG', true)
  expect(
    (await request.post(`${apiUrl}/api/requisitions/${requisition.id}/decision`, { headers: { 'X-Dev-User': 'dev-ceo' }, data: { approve: true, comment: null } })).status(),
  ).toBe(200)

  await signInAs(page, 'HrExecutive', 'PK')
  await page.goto(`/requisitions/${requisition.id}`)
  await page.getByRole('button', { name: 'Put on hold' }).click()
  await page.getByRole('dialog').getByLabel('Comment').fill('Hiring freeze')
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click()
  await expect(page.getByText('On hold', { exact: true }).first()).toBeVisible()

  await page.getByRole('button', { name: 'Reopen' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click()
  await expect(page.getByRole('table', { name: 'Status history' }).getByRole('row', { name: /On hold → Open/ })).toBeVisible()
})

test('NFR-02: managers only see their own department; employees get no requisitions at all', async ({ page, request }) => {
  const finance = await raise(request, 'HrExecutive', 'FIN', false)
  const engineering = await raise(request, 'HiringManager', 'ENG', false)

  await signInAs(page, 'LineManager', 'PK')
  await openNav(page, 'Requisitions')
  const table = page.getByRole('table', { name: 'Requisitions' })
  await expect(table.getByRole('link', { name: `${engineering.number} ${engineering.title}` })).toBeVisible()
  await expect(table.getByRole('link', { name: `${finance.number} ${finance.title}` })).toHaveCount(0)

  const direct = await request.get(`${apiUrl}/api/requisitions/${finance.id}`, { headers: asUser('LineManager', 'PK') })
  expect(direct.status()).toBe(403)
  await switchUser(page, devName('LineManager', 'PK'))

  await signInAs(page, 'Employee', 'PK')
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Requisitions' })).toHaveCount(0)
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Approvals' })).toHaveCount(0)
})

test('REQ-02: HR sees the approval chains; only HR admins can change them', async ({ page }) => {
  await signInAs(page, 'HrExecutive', 'PK')
  await openNav(page, 'Approval chains')
  const pakistan = page.getByLabel('Pakistan approval chain')
  await expect(pakistan.getByRole('textbox', { name: 'Step 1' })).toHaveValue('CEO')
  await expect(pakistan.getByRole('combobox', { name: 'Approver' })).toContainText('Dev CEO')
  await expect(pakistan.getByRole('button', { name: /Save/ })).toHaveCount(0)
  await switchUser(page, devName('HrExecutive', 'PK'))

  await signInAs(page, 'HrAdmin', 'PK')
  await openNav(page, 'Approval chains')
  const chain = page.getByLabel('Pakistan approval chain')
  await expect(chain.getByRole('button', { name: 'Save Pakistan chain' })).toBeVisible()
  // A chain always keeps at least one step (the API refuses an empty chain too).
  await expect(chain.getByRole('button', { name: 'Remove step 1' })).toBeDisabled()
  await chain.getByRole('button', { name: 'Add step' }).click()
  await expect(chain.getByRole('button', { name: 'Remove step 1' })).toBeEnabled()
  await chain.getByRole('button', { name: 'Remove step 2' }).click()
  await expect(chain.getByRole('button', { name: 'Remove step 1' })).toBeDisabled()
})
