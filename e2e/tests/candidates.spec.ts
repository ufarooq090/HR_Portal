import { type APIRequestContext, expect, type Page, test } from '@playwright/test'
import { apiUrl, asUser, devName, openNav, signInAs, switchUser, unique } from './helpers'

const ceo = { 'X-Dev-User': 'dev-ceo' }

/** An open requisition: raised by the PK hiring manager and approved by the CEO through the API. */
async function openRequisition(request: APIRequestContext): Promise<{ id: string; number: string }> {
  const headers = asUser('HiringManager', 'PK')
  const get = async <T>(path: string) => (await (await request.get(`${apiUrl}${path}`, { headers })).json()) as T
  const departments = await get<{ id: string; code: string }[]>('/api/organisation/departments')
  const offices = await get<{ id: string; name: string }[]>('/api/organisation/locations')
  const created = (await (
    await request.post(`${apiUrl}/api/requisitions`, {
      headers,
      data: {
        title: `ML Engineer ${unique()}`,
        departmentId: departments.find((d) => d.code === 'ENG')!.id,
        officeLocationId: offices.find((o) => o.name === 'Lahore Office')!.id,
        openings: 1,
        budgetMin: 3_000_000,
        budgetMax: 4_000_000,
        reason: 'NewPosition',
        replacedEmployeeId: null,
        targetDate: '2030-06-30',
        jobDescription: 'Models.',
      },
    })
  ).json()) as { id: string; number: string }
  await request.post(`${apiUrl}/api/requisitions/${created.id}/submit`, { headers, data: {} })
  expect((await request.post(`${apiUrl}/api/requisitions/${created.id}/decision`, { headers: ceo, data: { approve: true, comment: null } })).status()).toBe(200)
  return created
}

async function addCandidate(request: APIRequestContext, requisitionId: string, name: string, email: string) {
  const response = await request.post(`${apiUrl}/api/requisitions/${requisitionId}/applications`, {
    headers: asUser('HrExecutive', 'PK'),
    data: {
      personId: null,
      source: 'Referral',
      candidate: {
        givenName: name,
        familyName: 'Candidate',
        email,
        phone: null,
        currentEmployer: 'Globex',
        noticePeriodDays: 30,
        currentCtc: { amount: 2_500_000, currency: 'PKR' },
        expectedCtc: { amount: 3_200_000, currency: 'PKR' },
      },
    },
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string; personId: string }
}

async function choose(page: Page, field: string, option: string) {
  await page.getByRole('dialog').getByRole('combobox', { name: field }).click()
  await page.getByRole('option', { name: option, exact: true }).click()
}

const column = (page: Page, name: string) => page.getByRole('region', { name: `${name} column` })

test('CT-01/02/03: HR adds a candidate, drags them to screening and rejects with a reason', async ({ page, request }) => {
  test.slow()
  const requisition = await openRequisition(request)
  const name = `Hina${unique()}`

  await signInAs(page, 'HrExecutive', 'PK')
  await page.goto(`/requisitions/${requisition.id}/pipeline`)
  await page.getByRole('button', { name: 'Add candidate' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Given name').fill(name)
  await dialog.getByLabel('Family name').fill('Raza')
  await dialog.getByLabel('Email').fill(`${name.toLowerCase()}@mail.test`)
  await dialog.getByLabel('Current employer').fill('Initech')
  await dialog.getByLabel('Expected CTC (PKR)').fill('3,500,000')
  await dialog.getByRole('button', { name: 'Add as new candidate' }).click()

  await expect(column(page, 'Applied').getByText(`${name} Raza`)).toBeVisible()

  // CT-01: drag-and-drop between stages.
  await column(page, 'Applied').getByText('Initech').dragTo(column(page, 'Screening'))
  await expect(page.getByRole('dialog')).toContainText(`Move ${name} Raza to Screening`)
  await page.getByRole('dialog').getByLabel('Reason').fill('Strong CV')
  await page.getByRole('dialog').getByRole('button', { name: 'Move' }).click()
  await expect(column(page, 'Screening').getByText(`${name} Raza`)).toBeVisible()

  // CT-03: a rejection needs a reason from the list.
  await column(page, 'Screening').getByRole('button', { name: `Move ${name} Raza` }).click()
  await page.getByRole('menuitem', { name: 'Move to Rejected' }).click()
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Move' })).toBeDisabled()
  await choose(page, 'Rejection reason', 'Notice period too long')
  await page.getByRole('dialog').getByRole('button', { name: 'Move' }).click()
  await expect(column(page, 'Rejected').getByText(`${name} Raza`)).toBeVisible()

  await column(page, 'Rejected').getByRole('link', { name: `${name} Raza` }).click()
  const history = page.getByRole('table', { name: 'Stage history' })
  await expect(history).toContainText('Screening → Rejected')
  await expect(history).toContainText('Notice period too long')
  await expect(history).toContainText('Applied → Screening')
  await expect(history).toContainText('Strong CV')
})

test('CT-09: adding someone already in the portal links to the existing person', async ({ page, request }) => {
  const first = await openRequisition(request)
  const second = await openRequisition(request)
  const name = `Omar${unique()}`
  await addCandidate(request, first.id, name, `${name.toLowerCase()}@mail.test`)

  await signInAs(page, 'HrExecutive', 'PK')
  await page.goto(`/requisitions/${second.id}/pipeline`)
  await page.getByRole('button', { name: 'Add candidate' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Given name').fill('Someone')
  await dialog.getByLabel('Family name').fill('Else')
  await dialog.getByLabel('Email').fill(`  ${name.toUpperCase()}@MAIL.TEST`)
  await dialog.getByLabel('Phone').click()

  await expect(dialog.getByText('Already in the portal')).toBeVisible()
  await dialog.getByRole('button', { name: `Link ${name} Candidate` }).click()
  await expect(column(page, 'Applied').getByText(`${name} Candidate`)).toBeVisible()
})

test('CT-04/06/07: interview panel gives feedback; others see it only after submitting their own', async ({ page, request }) => {
  test.slow()
  const requisition = await openRequisition(request)
  const name = `Zara${unique()}`
  const application = await addCandidate(request, requisition.id, name, `${name.toLowerCase()}@mail.test`)

  await signInAs(page, 'HrExecutive', 'PK')
  await page.goto(`/applications/${application.id}`)
  await page.getByRole('button', { name: 'Add interview' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Date and time').fill('2030-01-15T10:00')
  await dialog.getByRole('combobox', { name: 'Panel' }).click()
  // A multi-select dropdown exposes its options as checkable menu items.
  await page.getByRole('menuitemcheckbox', { name: /Dev Interviewer \(PK\)/ }).click()
  await page.getByRole('menuitemcheckbox', { name: /Dev Employee \(PK\)/ }).click()
  await page.keyboard.press('Escape')
  await dialog.getByRole('button', { name: 'Save interview' }).click()
  await expect(page.getByLabel('Interview round 1')).toContainText('Panel: Dev Employee (PK), Dev Interviewer (PK)')
  await switchUser(page, devName('HrExecutive', 'PK'))

  // The interviewer finds it under My interviews and submits a scorecard.
  await signInAs(page, 'Interviewer', 'PK')
  await openNav(page, 'My interviews')
  await page.getByRole('row', { name: new RegExp(name) }).getByRole('button', { name: 'Give feedback' }).click()
  await expect(page.getByText('Expected CTC')).toHaveCount(0)
  await page.getByRole('button', { name: 'Submit feedback' }).click()
  for (const criterion of ['Technical skills', 'Problem solving', 'Communication', 'Team fit']) {
    await choose(page, `${criterion} rating`, '4')
  }
  await choose(page, 'Recommendation', 'Strong hire')
  await page.getByRole('dialog').getByLabel('Overall comments').fill('Excellent system design.')
  await page.getByRole('dialog').getByRole('button', { name: 'Submit feedback' }).click()
  await expect(page.getByLabel('Feedback from Dev Interviewer (PK)')).toContainText('Excellent system design.')
  await switchUser(page, devName('Interviewer', 'PK'))

  // The other panelist cannot see it until they submit their own (CT-07).
  await signInAs(page, 'Employee', 'PK')
  await page.goto(`/applications/${application.id}`)
  await expect(page.getByText('1 of the panel have submitted. You will see their feedback once you submit yours.')).toBeVisible()
  await expect(page.getByLabel('Feedback from Dev Interviewer (PK)')).toHaveCount(0)
  await switchUser(page, devName('Employee', 'PK'))

  // HR sees all feedback.
  await signInAs(page, 'HrExecutive', 'PK')
  await page.goto(`/applications/${application.id}`)
  await expect(page.getByLabel('Feedback from Dev Interviewer (PK)')).toContainText('Strong hire')
})

test('NFR-02: hiring managers see their requisition with salary and notes; interviewers only assigned candidates; line managers nothing', async ({ page, request }) => {
  const requisition = await openRequisition(request)
  const name = `Ali${unique()}`
  const application = await addCandidate(request, requisition.id, name, `${name.toLowerCase()}@mail.test`)
  await request.post(`${apiUrl}/api/applications/${application.id}/notes`, { headers: asUser('HrExecutive', 'PK'), data: { body: 'Referred by the CTO.' } })

  await signInAs(page, 'HiringManager', 'PK')
  await page.goto(`/applications/${application.id}`)
  await expect(page.getByText('PKR 3,200,000')).toBeVisible()
  await expect(page.getByText('Referred by the CTO.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Move to…' })).toHaveCount(0)
  await switchUser(page, devName('HiringManager', 'PK'))

  await signInAs(page, 'Interviewer', 'PK')
  await openNav(page, 'Candidates')
  await expect(page.getByRole('link', { name: `${name} Candidate` })).toHaveCount(0)
  expect((await request.get(`${apiUrl}/api/applications/${application.id}`, { headers: asUser('Interviewer', 'PK') })).status()).toBe(403)
  await switchUser(page, devName('Interviewer', 'PK'))

  await signInAs(page, 'LineManager', 'PK')
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Candidates' })).toHaveCount(0)
  expect((await request.get(`${apiUrl}/api/applications/${application.id}`, { headers: asUser('LineManager', 'PK') })).status()).toBe(403)
})

test('CT-03: HR sees the rejection reasons list; only HR admins change it', async ({ page }) => {
  await signInAs(page, 'HrExecutive', 'PK')
  await openNav(page, 'Rejection reasons')
  await expect(page.getByRole('table', { name: 'Rejection reasons' })).toContainText('Salary expectations too high')
  await expect(page.getByRole('button', { name: 'Add rejection reason' })).toHaveCount(0)
  await switchUser(page, devName('HrExecutive', 'PK'))

  await signInAs(page, 'HrAdmin', 'PK')
  await openNav(page, 'Rejection reasons')
  await expect(page.getByRole('button', { name: 'Add rejection reason' })).toBeVisible()
})
