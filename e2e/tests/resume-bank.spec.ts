import { type APIRequestContext, expect, type Page, test } from '@playwright/test'
import { apiUrl, asUser, devName, openNav, signInAs, switchUser, unique } from './helpers'

const header =
  'GivenName,FamilyName,Email,Phone,Country,Location,CurrentEmployer,ExperienceYears,NoticePeriodDays,Skills,Tags,CurrentCtc,ExpectedCtc,CtcCurrency,Status,LastContacted,Notes'

const csv = (...rows: string[]) => ({ name: 'candidates.csv', mimeType: 'text/csv', buffer: Buffer.from([header, ...rows].join('\n')) })

/** A made-up lower-case word, so searches only match this run's data. */
const word = () => `zq${unique().toLowerCase().replace(/[^a-z]/g, 'k')}x`

/** A one-page PDF with real text, so the server's text extraction (PdfPig) and full-text search can find its words. */
function pdf(text: string): Buffer {
  const content = `BT /F1 12 Tf 72 720 Td (${text.replace(/[()\\]/g, '')}) Tj ET`
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${content.length} >>\nstream\n${content}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ]
  let body = '%PDF-1.4\n'
  const offsets: number[] = []
  objects.forEach((o, i) => {
    offsets.push(body.length)
    body += `${i + 1} 0 obj\n${o}\nendobj\n`
  })
  const xref = body.length
  body += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map((o) => `${String(o).padStart(10, '0')} 00000 n \n`).join('')}`
  body += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`
  return Buffer.from(body, 'latin1')
}

/** Imports one PK candidate through the API and returns their person id. */
async function importCandidate(request: APIRequestContext, given: string, skill: string, extra = ',,,,,'): Promise<string> {
  const row = `${given},Bank,${given.toLowerCase()}@mail.test,,PK,Lahore,Globex,6,30,"${skill}; sql",,2500000${extra}`
  const response = await request.post(`${apiUrl}/api/resume-bank/import`, { headers: asUser('HrExecutive', 'PK'), multipart: { file: csv(row) } })
  expect(response.status()).toBe(200)
  expect(((await response.json()) as { imported: number }).imported).toBe(1)
  const search = (await (await request.get(`${apiUrl}/api/resume-bank?skills=${skill}`, { headers: asUser('HrExecutive', 'PK') })).json()) as {
    items: { personId: string; name: string }[]
  }
  return search.items.find((i) => i.name === `${given} Bank`)!.personId
}

async function openRequisition(request: APIRequestContext): Promise<{ id: string; number: string; title: string }> {
  const headers = asUser('HiringManager', 'PK')
  const get = async <T>(path: string) => (await (await request.get(`${apiUrl}${path}`, { headers })).json()) as T
  const departments = await get<{ id: string; code: string }[]>('/api/organisation/departments')
  const offices = await get<{ id: string; name: string }[]>('/api/organisation/locations')
  const title = `Data Analyst ${unique()}`
  const created = (await (
    await request.post(`${apiUrl}/api/requisitions`, {
      headers,
      data: {
        title,
        departmentId: departments.find((d) => d.code === 'ENG')!.id,
        officeLocationId: offices.find((o) => o.name === 'Lahore Office')!.id,
        openings: 1,
        budgetMin: 2_000_000,
        budgetMax: 3_000_000,
        reason: 'NewPosition',
        replacedEmployeeId: null,
        targetDate: '2030-06-30',
        jobDescription: 'Dashboards.',
      },
    })
  ).json()) as { id: string; number: string }
  await request.post(`${apiUrl}/api/requisitions/${created.id}/submit`, { headers, data: {} })
  const approved = await request.post(`${apiUrl}/api/requisitions/${created.id}/decision`, { headers: { 'X-Dev-User': 'dev-ceo' }, data: { approve: true, comment: null } })
  expect(approved.status()).toBe(200)
  return { ...created, title }
}

const results = (page: Page) => page.getByRole('table', { name: 'Resume Bank candidates' })

test('RB-12/04: HR imports a candidate sheet, fixes errors, and finds candidates by skill and by words inside their CV', async ({ page }) => {
  test.slow()
  const skill = word()
  const cvWord = word()
  const [a, b] = [`Areeba${unique()}`, `Bilal${unique()}`]
  const row = (given: string, email: string, experience: string) =>
    `${given},Bank,${email},,PK,Lahore,Globex,${experience},30,"${skill}; Python",Future data roles,2500000,,,Shortlisted,2026-09-01,Met at a job fair`

  await signInAs(page, 'HrExecutive', 'PK')
  await openNav(page, 'Resume Bank')
  await page.getByRole('button', { name: 'Import candidates' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Import candidates' })).toBeVisible()

  // The same email twice: nothing is imported and the problem is listed.
  await page.getByLabel('Spreadsheet').setInputFiles(csv(row(a, `${a}@mail.test`, '7'), row(b, `${a}@mail.test`, '2')))
  await page.getByRole('button', { name: 'Import' }).click()
  await expect(page.getByText('Nothing was imported.')).toBeVisible()
  await expect(page.getByRole('table', { name: 'Import problems' })).toContainText('Also used on row 2')
  const report = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download error report' }).click()
  expect((await report).suggestedFilename()).toBe('candidate-import-errors.xlsx')

  await page.getByLabel('Spreadsheet').setInputFiles(csv(row(a, `${a}@mail.test`, '7'), row(b, `${b}@mail.test`, '2')))
  await page.getByRole('button', { name: 'Import' }).click()
  await expect(page.getByText('Imported 2 candidates.')).toBeVisible()

  // Filters: skill, then experience.
  await page.getByRole('link', { name: 'View them in Resume Bank' }).click()
  await page.getByLabel('Skills (all of)').fill(skill)
  await expect(results(page).getByRole('link', { name: `${a} Bank` })).toBeVisible()
  await expect(results(page).getByRole('link', { name: `${b} Bank` })).toBeVisible()
  await page.getByLabel('Min. experience (years)').fill('5')
  await expect(results(page).getByRole('link', { name: `${b} Bank` })).toBeHidden()
  await expect(results(page)).toContainText('PKR')

  // Upload a CV on the profile, then find the candidate by a word inside it.
  await results(page).getByRole('link', { name: `${a} Bank` }).click()
  await expect(page.getByRole('heading', { level: 1, name: `${a} Bank` })).toBeVisible()
  await expect(page.getByLabel('History')).toContainText('Met at a job fair')
  await page.getByLabel('Upload CV').setInputFiles({ name: 'areeba-cv.pdf', mimeType: 'application/pdf', buffer: pdf(`Built ${cvWord} pipelines on Spark`) })
  await expect(page.getByRole('button', { name: 'areeba-cv.pdf' })).toBeVisible()

  await page.getByRole('link', { name: 'Back to Resume Bank' }).click()
  await page.getByRole('searchbox', { name: 'Search CVs, names and employers' }).fill(`${cvWord} pipeline`)
  await expect(results(page).getByRole('row')).toHaveCount(2)
  await expect(results(page).getByRole('link', { name: `${a} Bank` })).toBeVisible()
})

test('RB-03: only an HR Admin can blacklist, with a reason; blacklisted candidates cannot be added to requisitions', async ({ page, request }) => {
  test.slow()
  const given = `Kamal${unique()}`
  const personId = await importCandidate(request, given, word())

  await signInAs(page, 'HrExecutive', 'PK')
  await page.goto(`/resume-bank/${personId}`)
  await expect(page.getByRole('heading', { level: 1, name: `${given} Bank` })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Add to requisition' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Blacklist' })).toBeHidden()

  await switchUser(page, devName('HrExecutive', 'PK'))
  await page.getByRole('button', { name: `Sign in as ${devName('HrAdmin', 'PK')}` }).click()
  await page.goto(`/resume-bank/${personId}`)
  await page.getByRole('button', { name: 'Blacklist' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('button', { name: 'Blacklist' })).toBeDisabled()
  await dialog.getByLabel('Reason').fill('Forged experience letters')
  await dialog.getByRole('button', { name: 'Blacklist' }).click()

  await expect(page.getByText('Blacklisted: this candidate cannot be added to requisitions. Reason: Forged experience letters')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Add to requisition' })).toBeHidden()
  await expect(page.getByLabel('History')).toContainText('Resume Bank status: Blacklisted')

  // The API refuses too, whatever the UI shows.
  const requisition = await openRequisition(request)
  const add = await request.post(`${apiUrl}/api/requisitions/${requisition.id}/applications`, {
    headers: asUser('HrExecutive', 'PK'),
    data: { personId, candidate: null, source: 'Referral' },
  })
  expect(add.status()).toBe(400)

  await page.getByRole('button', { name: 'Remove from blacklist' }).click()
  await page.getByRole('dialog').getByLabel('Reason').fill('Letters verified')
  await page.getByRole('dialog').getByRole('button', { name: 'Remove' }).click()
  await expect(page.getByRole('button', { name: 'Add to requisition' })).toBeVisible()
})

test('RB-08 and scoping: HR adds a candidate to a requisition in one click; other hiring managers see CVs and outcomes but not contact, salary or notes', async ({
  page,
  request,
}) => {
  test.slow()
  const given = `Hira${unique()}`
  const personId = await importCandidate(request, given, word(), ',,,,,Prefers hybrid work')
  await request.post(`${apiUrl}/api/candidates/${personId}/cvs`, {
    headers: asUser('HrExecutive', 'PK'),
    multipart: { file: { name: 'hira-cv.pdf', mimeType: 'application/pdf', buffer: pdf('Analyst with Power BI') } },
  })
  const requisition = await openRequisition(request)

  await signInAs(page, 'HrExecutive', 'PK')
  await page.goto(`/resume-bank/${personId}`)
  await expect(page.getByText(`${given.toLowerCase()}@mail.test`)).toBeVisible()
  await page.getByRole('combobox', { name: 'Open requisition' }).click()
  await page.getByRole('option', { name: `${requisition.number} ${requisition.title} (PK)` }).click()
  await page.getByRole('button', { name: 'Add to requisition' }).click()
  await expect(page.getByRole('status')).toContainText('Added.')
  await expect(page.getByRole('table', { name: 'Applications' })).toContainText(requisition.number)

  await switchUser(page, devName('HrExecutive', 'PK'))
  await page.getByRole('button', { name: `Sign in as ${devName('HiringManager', 'US')}` }).click()
  await openNav(page, 'Resume Bank')
  await page.getByRole('searchbox', { name: 'Search CVs, names and employers' }).fill(`${given} Bank`)
  await results(page).getByRole('link', { name: `${given} Bank` }).click()

  await expect(page.getByRole('heading', { level: 1, name: `${given} Bank` })).toBeVisible()
  await expect(page.getByRole('button', { name: 'hira-cv.pdf' })).toBeVisible()
  await expect(page.getByRole('table', { name: 'Applications' })).toContainText(requisition.number)
  await expect(page.getByRole('table', { name: 'Applications' }).getByRole('link')).toHaveCount(0)
  await expect(page.getByText(`${given.toLowerCase()}@mail.test`)).toBeHidden()
  await expect(page.getByText('Current CTC')).toBeHidden()
  await expect(page.getByLabel('History')).not.toContainText('Prefers hybrid work')
  await expect(page.getByRole('button', { name: 'Set status…' })).toBeHidden()
  await expect(page.getByRole('heading', { name: 'Add a note' })).toBeHidden()

  // The requisition's own hiring manager sees the salary.
  await switchUser(page, devName('HiringManager', 'US'))
  await page.getByRole('button', { name: `Sign in as ${devName('HiringManager', 'PK')}` }).click()
  await page.goto(`/resume-bank/${personId}`)
  await expect(page.getByText(/PKR\s2,500,000/).first()).toBeVisible()
})

test('Resume Bank is not offered to line managers, interviewers or employees', async ({ page, request }) => {
  await signInAs(page, 'Employee', 'PK')
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Resume Bank' })).toBeHidden()
  for (const role of ['LineManager', 'Interviewer', 'Employee']) {
    expect((await request.get(`${apiUrl}/api/resume-bank`, { headers: asUser(role, 'PK') })).status()).toBe(403)
  }
})
