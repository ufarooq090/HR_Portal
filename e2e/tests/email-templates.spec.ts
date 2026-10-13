import { type APIRequestContext, expect, type Page, test } from '@playwright/test'
import { apiUrl, asUser, devName, openNav, signInAs, switchUser, unique } from './helpers'

const mailpit = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'

/** An open PK requisition raised by the hiring manager and approved by the CEO. */
async function openRequisition(request: APIRequestContext): Promise<{ id: string; number: string }> {
  const headers = asUser('HiringManager', 'PK')
  const get = async <T>(path: string) => (await (await request.get(`${apiUrl}${path}`, { headers })).json()) as T
  const departments = await get<{ id: string; code: string }[]>('/api/organisation/departments')
  const offices = await get<{ id: string; name: string }[]>('/api/organisation/locations')
  const created = (await (
    await request.post(`${apiUrl}/api/requisitions`, {
      headers,
      data: {
        title: `QA Engineer ${unique()}`,
        departmentId: departments.find((d) => d.code === 'ENG')!.id,
        officeLocationId: offices.find((o) => o.name === 'Lahore Office')!.id,
        openings: 3,
        budgetMin: 2_000_000,
        budgetMax: 3_000_000,
        reason: 'NewPosition',
        replacedEmployeeId: null,
        targetDate: '2030-06-30',
        jobDescription: 'Testing.',
      },
    })
  ).json()) as { id: string; number: string }
  await request.post(`${apiUrl}/api/requisitions/${created.id}/submit`, { headers, data: {} })
  const approved = await request.post(`${apiUrl}/api/requisitions/${created.id}/decision`, { headers: { 'X-Dev-User': 'dev-ceo' }, data: { approve: true, comment: null } })
  expect(approved.status()).toBe(200)
  return created
}

async function addCandidate(request: APIRequestContext, requisitionId: string, givenName: string, email: string | null) {
  const response = await request.post(`${apiUrl}/api/requisitions/${requisitionId}/applications`, {
    headers: asUser('HrExecutive', 'PK'),
    data: {
      personId: null,
      source: 'Referral',
      candidate: {
        givenName,
        familyName: 'Mailtest',
        email,
        phone: email ? null : `0301${Math.floor(1_000_000 + Math.random() * 8_999_999)}`,
        currentEmployer: 'Globex',
        noticePeriodDays: 30,
        currentCtc: null,
        expectedCtc: null,
      },
    },
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string; personId: string }
}

async function scheduleInterview(request: APIRequestContext, applicationId: string) {
  const headers = asUser('HrAdmin', 'PK')
  const employees = (await (await request.get(`${apiUrl}/api/employees`, { headers })).json()) as { id: string; employeeNumber: string }[]
  const response = await request.post(`${apiUrl}/api/applications/${applicationId}/interviews`, {
    headers: asUser('HrExecutive', 'PK'),
    data: {
      round: 1,
      startUtc: '2030-11-04T10:00:00Z',
      durationMinutes: 60,
      mode: 'Teams',
      location: 'Teams link',
      panelEmployeeIds: [employees.find((e) => e.employeeNumber === 'PK-0007')!.id],
    },
  })
  expect(response.ok()).toBeTruthy()
}

/** The HTML of the newest email Mailpit received for this address. */
async function mailTo(request: APIRequestContext, address: string): Promise<{ subject: string; html: string; count: number }> {
  let found: { ID: string; Subject: string }[] = []
  await expect
    .poll(async () => {
      const search = (await (await request.get(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${address}"`)}`)).json()) as {
        messages: { ID: string; Subject: string }[]
      }
      found = search.messages
      return found.length
    })
    .toBeGreaterThan(0)
  const message = (await (await request.get(`${mailpit}/api/v1/message/${found[0].ID}`)).json()) as { HTML: string }
  return { subject: found[0].Subject, html: message.HTML, count: found.length }
}

async function chooseTemplate(page: Page, name: string) {
  await page.getByRole('dialog').getByRole('combobox', { name: 'Template' }).click()
  await page.getByRole('option', { name: new RegExp(`^${name} \\(`) }).click()
}

test('ET-01/06: an HR Admin writes a template with merge fields, and every save is a new version', async ({ page }) => {
  test.slow()
  const name = `Thank you ${unique()}`

  await signInAs(page, 'HrAdmin', 'PK')
  await openNav(page, 'Email templates')
  await expect(page.getByRole('table', { name: 'Email templates' })).toContainText('Termination communication')
  await page.getByRole('button', { name: 'New template' }).click()

  await page.getByLabel('Name').fill(name)
  await page.getByRole('combobox', { name: 'Category' }).click()
  await page.getByRole('option', { name: 'Rejection', exact: true }).click()
  await page.getByLabel('Subject').fill('Thanks for applying for {{designation}}')
  const body = page.getByRole('textbox', { name: 'Body' })
  await body.click()
  await page.keyboard.type('Dear ')
  await page.getByRole('button', { name: 'Insert field' }).click()
  await page.getByRole('menuitem', { name: 'First name' }).click()
  await page.keyboard.type(', thank you. ')
  await page.getByRole('button', { name: 'Bold' }).click()
  await page.keyboard.type('We will be in touch.')
  await page.getByRole('button', { name: 'Create template' }).click()

  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
  await expect(page.getByText('Version 1', { exact: true })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Body' }).locator('strong')).toHaveText('We will be in touch.')
  await expect(page.getByRole('textbox', { name: 'Body' })).toContainText('Dear {{first_name}}, thank you.')

  await page.getByLabel('Subject').fill('Your application for {{designation}}')
  await page.getByRole('button', { name: 'Save new version' }).click()
  await expect(page.getByText('Version 2', { exact: true })).toBeVisible()
  const history = page.getByRole('table', { name: 'Version history' })
  await expect(history.getByRole('row')).toHaveCount(3)
  await history.getByRole('button', { name: 'View version 1' }).click()
  await expect(page.getByRole('dialog')).toContainText('Thanks for applying for {{designation}}')
})

test('ET-02/03/04/08: HR fill an interview email from the record, edit it, test it, send it, and it is logged', async ({ page, request }) => {
  test.slow()
  const requisition = await openRequisition(request)
  const given = `Mahnoor${unique()}`
  const email = `${given.toLowerCase()}@mail.test`
  const scheduled = await addCandidate(request, requisition.id, given, email)
  await scheduleInterview(request, scheduled.id)
  const unscheduled = await addCandidate(request, requisition.id, `Waqar${unique()}`, `waqar.${unique().toLowerCase()}@mail.test`)

  await signInAs(page, 'HrExecutive', 'PK')

  // No interview yet: the interview fields have no value, so sending is blocked.
  await page.goto(`/applications/${unscheduled.id}`)
  await page.getByRole('button', { name: 'Send email' }).click()
  await chooseTemplate(page, 'Interview confirmation')
  await expect(page.getByRole('dialog')).toContainText('This email cannot be sent yet')
  await expect(page.getByRole('dialog')).toContainText('Interview date')
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Send', exact: true })).toBeDisabled()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()

  await page.goto(`/applications/${scheduled.id}`)
  await page.getByRole('button', { name: 'Send email' }).click()
  await chooseTemplate(page, 'Interview confirmation')
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText(`To: ${email}`)
  await expect(dialog.getByLabel('Subject')).toHaveValue(/Your interview for QA Engineer .* on Mon 4 Nov 2030/)
  const message = dialog.getByRole('textbox', { name: 'Message' })
  await expect(message).toContainText('15:00 Asia/Karachi (UTC+05:00)')
  await expect(message).toContainText(`Dear ${given},`)

  // ET-04: edit the filled email.
  await message.getByText('If you need to change the time').click()
  await page.keyboard.press('End')
  await page.keyboard.type(' Please bring your CNIC.')
  await dialog.getByRole('button', { name: 'Send test to myself' }).click()
  await expect(dialog).toContainText('Test sent to')
  await dialog.getByRole('button', { name: 'Send', exact: true }).click()
  await expect(dialog).toContainText(`Email sent to ${email}.`)
  await dialog.getByRole('button', { name: 'Close' }).click()

  const received = await mailTo(request, email)
  expect(received.subject).toMatch(/^Your interview for QA Engineer/)
  expect(received.html).toContain('Please bring your CNIC.')
  expect(received.html).toContain('Dev Interviewer (PK)')

  // ET-08: logged on the candidate's record.
  const emails = page.getByRole('table', { name: 'Emails sent' })
  await expect(emails).toContainText('Interview confirmation (v')
  await emails.getByRole('button', { name: /Your interview for QA Engineer/ }).click()
  await expect(page.getByRole('document', { name: 'Email message' })).toContainText('Please bring your CNIC.')
})

test('ET-05: HR email a rejection to several candidates at once and see what was sent and what was not', async ({ page, request }) => {
  test.slow()
  const requisition = await openRequisition(request)
  const tag = unique()
  await addCandidate(request, requisition.id, `Bulk${tag}A`, `bulk${tag.toLowerCase()}a@mail.test`)
  await addCandidate(request, requisition.id, `Bulk${tag}B`, `bulk${tag.toLowerCase()}b@mail.test`)
  await addCandidate(request, requisition.id, `Bulk${tag}C`, null)

  await signInAs(page, 'HrExecutive', 'PK')
  await openNav(page, 'Candidates')
  await page.getByRole('searchbox', { name: 'Search candidates' }).fill(`Bulk${tag}`)
  await expect(page.getByRole('table', { name: 'Candidates' }).getByRole('row')).toHaveCount(4)
  await page.getByRole('checkbox', { name: 'Select all candidates' }).check()
  await page.getByRole('button', { name: 'Email selected (3)' }).click()
  await chooseTemplate(page, 'Rejection notification')
  await page.getByRole('dialog').getByRole('button', { name: 'Send 3' }).click()

  await expect(page.getByRole('dialog')).toContainText('Sent 2, not sent 1.')
  const results = page.getByRole('table', { name: 'Bulk send results' })
  await expect(results.getByRole('row', { name: new RegExp(`Bulk${tag}C`) })).toContainText('has no email address')
  const a = await mailTo(request, `bulk${tag.toLowerCase()}a@mail.test`)
  expect(a.html).toContain(`Dear Bulk${tag}A,`)
})

test('Rule 8 and access: termination emails are HR Admin only and never bulk; templates are not offered to other roles', async ({ page, request }) => {
  await signInAs(page, 'HrExecutive', 'PK')
  await openNav(page, 'Email templates')
  await expect(page.getByRole('table', { name: 'Email templates' })).toContainText('Rejection notification')
  await expect(page.getByRole('table', { name: 'Email templates' })).not.toContainText('Termination communication')

  await switchUser(page, devName('HrExecutive', 'PK'))
  await page.getByRole('button', { name: `Sign in as ${devName('HiringManager', 'PK')}` }).click()
  await openNav(page, 'Email templates')
  await expect(page.getByRole('table', { name: 'Email templates' })).toContainText('Interview confirmation')
  await expect(page.getByRole('table', { name: 'Email templates' })).not.toContainText('Rejection notification')

  await switchUser(page, devName('HiringManager', 'PK'))
  await page.getByRole('button', { name: `Sign in as ${devName('Employee', 'PK')}` }).click()
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Email templates' })).toBeHidden()

  const templates = (await (await request.get(`${apiUrl}/api/email-templates`, { headers: asUser('HrAdmin', 'PK') })).json()) as { id: string; name: string }[]
  const termination = templates.find((t) => t.name === 'Termination communication')!.id
  const employees = (await (await request.get(`${apiUrl}/api/employees`, { headers: asUser('HrAdmin', 'PK') })).json()) as { id: string; employeeNumber: string }[]
  const junior = employees.find((e) => e.employeeNumber === 'PK-0008')!.id
  const bulk = await request.post(`${apiUrl}/api/emails/bulk`, {
    headers: asUser('HrAdmin', 'PK'),
    data: { templateId: termination, recordIds: [junior], sender: 'HrMailbox', attachmentIds: [] },
  })
  expect(bulk.status()).toBe(403)
  const asExecutive = await request.post(`${apiUrl}/api/emails/preview`, {
    headers: asUser('HrExecutive', 'PK'),
    data: { templateId: termination, recordId: junior, subject: null, bodyHtml: null },
  })
  expect(asExecutive.status()).toBe(403)
})
