import { expect, type Page, test } from '@playwright/test'
import { apiUrl, asUser, createEmployee, devName, openNav, rowFor, signInAs, unique } from './helpers'

async function choose(page: Page, field: string, option: string | RegExp) {
  await page.getByRole('combobox', { name: field }).click()
  await page.getByRole('option', { name: option }).click()
}

async function openProfile(page: Page, name: string) {
  await openNav(page, 'People')
  await page.getByRole('searchbox', { name: 'Search people' }).fill(name)
  await page.getByRole('link', { name, exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
}

test('EMP-01/02/05: HR executive adds an employee, then records a promotion', async ({ page }) => {
  test.slow() // long form plus a dialog and three tabs
  const number = `PK-E2E-${unique()}`
  await signInAs(page, 'HrExecutive', 'PK')
  await openNav(page, 'People')
  await page.getByRole('button', { name: 'Add employee' }).click()

  await page.getByLabel('Given name').fill('Ayesha')
  await page.getByLabel('Family name').fill(`Khan ${number}`)
  await page.getByLabel('Employee ID').fill(number)
  await page.getByLabel('Work email').fill(`${number.toLowerCase()}@e2e.test`)
  await page.getByLabel('Designation').fill('Data Analyst')
  await choose(page, 'Legal entity', 'HR Portal Dev (Pvt) Ltd (PK)')
  await choose(page, 'Office', 'Lahore Office')
  await choose(page, 'Department', 'Engineering')
  await page.getByLabel('Joining date').fill('3 Mar 2025')
  await page.getByLabel('National ID (CNIC / SSN)').fill('35202-0000000-1')
  await page.getByRole('button', { name: 'Add emergency contact' }).click()
  await page.getByLabel('Contact 1 name').fill('Bilal Khan')
  await page.getByLabel('Relationship').fill('Brother')
  await page.getByRole('textbox', { name: 'Phone' }).last().fill('+92 300 1234567')
  await page.getByRole('button', { name: 'Add employee' }).click()

  await expect(page.getByRole('heading', { level: 1, name: `Ayesha Khan ${number}` })).toBeVisible()
  await expect(page.getByText('Probation', { exact: true })).toBeVisible()
  await expect(page.getByText('3 Jun 2025')).toBeVisible() // probation ends after the 3-month country default

  await page.getByRole('tab', { name: 'Personal' }).click()
  await expect(page.getByText('35202-0000000-1')).toBeVisible()
  await expect(page.getByText('Bilal Khan (Brother)')).toBeVisible()

  await page.getByRole('tab', { name: 'Job' }).click()
  await page.getByRole('button', { name: 'Change job' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Designation').fill('Senior Data Analyst')
  await dialog.getByLabel('Effective date').fill('2025-09-01')
  await dialog.getByLabel('Reason').fill('Strong first half')
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(dialog).toHaveCount(0)

  await page.getByRole('tab', { name: 'History' }).click()
  const history = page.getByRole('table', { name: 'Job history' })
  await expect(history.getByRole('row', { name: /Promotion/ })).toContainText('Senior Data Analyst')
  await expect(history.getByRole('row', { name: /Joined/ })).toContainText('Data Analyst')
})

test('EMP-04: HR executives cannot terminate; HR admins can', async ({ page, request }) => {
  const employee = await createEmployee(request, 'PK')

  await signInAs(page, 'HrExecutive', 'PK')
  await openProfile(page, employee.fullName)
  await page.getByRole('button', { name: 'Change status' }).click()
  await page.getByRole('combobox', { name: 'New status' }).click()
  await expect(page.getByRole('option', { name: 'Resigned' })).toBeVisible()
  await expect(page.getByRole('option', { name: 'Terminated' })).toHaveCount(0)
  await page.keyboard.press('Escape')
  await page.getByRole('button', { name: 'Cancel' }).click()

  const asExecutive = await request.post(`${apiUrl}/api/employees/${employee.id}/status`, {
    headers: asUser('HrExecutive', 'PK'),
    data: { status: 'Terminated', effectiveDate: '2025-06-01', lastWorkingDate: null, reason: null },
  })
  expect(asExecutive.status()).toBe(403)

  await page.getByRole('button', { name: devName('HrExecutive', 'PK') }).click()
  await page.getByRole('menuitem', { name: 'Switch user' }).click()
  await signInAs(page, 'HrAdmin', 'PK')
  await openProfile(page, employee.fullName)
  await page.getByRole('button', { name: 'Change status' }).click()
  await choose(page, 'New status', 'Terminated')
  await page.getByRole('dialog').getByLabel('Effective date').fill('2025-06-01')
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('Terminated', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Change status' })).toHaveCount(0)
})

test('EMP-03/MC-02: salary is visible to HR admins and the employee, not to managers or HR executives', async ({ page }) => {
  test.slow() // four sign-ins in one journey
  const employee = devName('Employee', 'PK')

  await signInAs(page, 'HrAdmin', 'PK')
  await openProfile(page, employee)
  await page.getByRole('tab', { name: 'Compensation' }).click()
  await expect(page.getByRole('table', { name: 'Compensation history' })).toContainText('PKR 3,600,000')
  await expect(page.getByText('Current', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: devName('HrAdmin', 'PK') }).click()
  await page.getByRole('menuitem', { name: 'Switch user' }).click()

  await signInAs(page, 'LineManager', 'PK')
  await openProfile(page, employee)
  await expect(page.getByRole('tab', { name: 'Job' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Compensation' })).toHaveCount(0)
  await expect(page.getByRole('tab', { name: 'Documents' })).toHaveCount(0)

  // HR decision: managers see their team's personal details (audited), never the national ID.
  await page.getByRole('tab', { name: 'Personal' }).click()
  await expect(page.getByText('Each view is recorded in the audit log.')).toBeVisible()
  await expect(page.getByText('+92 300 000 0006')).toBeVisible()
  await expect(page.getByText('National ID')).toHaveCount(0)
  await page.getByRole('button', { name: devName('LineManager', 'PK') }).click()
  await page.getByRole('menuitem', { name: 'Switch user' }).click()

  await signInAs(page, 'HrExecutive', 'PK')
  await openProfile(page, employee)
  await expect(page.getByRole('tab', { name: 'Personal' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Compensation' })).toHaveCount(0)
  await page.getByRole('button', { name: devName('HrExecutive', 'PK') }).click()
  await page.getByRole('menuitem', { name: 'Switch user' }).click()

  await signInAs(page, 'Employee', 'PK')
  await openProfile(page, employee)
  await page.getByRole('tab', { name: 'Compensation' }).click()
  await expect(page.getByRole('table', { name: 'Compensation history' })).toContainText('PKR 3,600,000')
  await expect(page.getByRole('button', { name: 'Add compensation' })).toHaveCount(0)
})

test('EMP-03: HR admin records an increment in the local currency', async ({ page, request }) => {
  const employee = await createEmployee(request, 'US')
  await signInAs(page, 'HrAdmin', 'US')
  await openProfile(page, employee.fullName)
  await page.getByRole('tab', { name: 'Compensation' }).click()
  await expect(page.getByText('No compensation recorded yet.')).toBeVisible()

  await page.getByRole('button', { name: 'Add compensation' }).click()
  await page.getByLabel('Annual CTC (USD)').fill('92,500')
  await page.getByLabel('Salary band').fill('B2')
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()

  await expect(page.getByRole('table', { name: 'Compensation history' })).toContainText('USD 92,500')
})

test('EMP-06: HR uploads a document and downloads it again', async ({ page, request }) => {
  test.slow()
  const employee = await createEmployee(request, 'PK')
  await signInAs(page, 'HrAdmin', 'PK')
  await openProfile(page, employee.fullName)
  await page.getByRole('tab', { name: 'Documents' }).click()
  await expect(page.getByText('No documents yet.')).toBeVisible()

  await page.getByLabel('File').setInputFiles({ name: 'offer-letter.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.7\n% e2e\n') })
  await choose(page, 'Category', 'Signed letter')
  await page.getByRole('button', { name: 'Upload' }).click()

  const row = page.getByRole('table', { name: 'Documents' }).getByRole('row', { name: /offer-letter\.pdf/ })
  await expect(row).toContainText('Signed letter')
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download offer-letter.pdf' }).click()
  expect((await download).suggestedFilename()).toBe('offer-letter.pdf')

  await page.getByLabel('File').setInputFiles({ name: 'tool.exe', mimeType: 'application/octet-stream', buffer: Buffer.from('MZ') })
  await page.getByRole('button', { name: 'Upload' }).click()
  await expect(page.getByRole('alert')).toContainText('Upload a PDF')

  page.once('dialog', (d) => void d.accept())
  await page.getByRole('button', { name: 'Delete offer-letter.pdf' }).click()
  await expect(row).toHaveCount(0)
})

test('EMP-07: import reports problems with a downloadable report, then imports a fixed file', async ({ page }) => {
  test.slow()
  const [a, b] = [`IMP-${unique()}`, `IMP-${unique()}`]
  const header = 'EmployeeNumber,GivenName,FamilyName,WorkEmail,Designation,EmploymentType,LegalEntity,Office,Department,ManagerEmployeeNumber,JoiningDate'
  const row = (n: string, department: string, manager = '') =>
    `${n},Imported,Person ${n},${n.toLowerCase()}@e2e.test,Analyst,Permanent,HR Portal Dev Inc.,US Office,${department},${manager},2024-02-01`
  const csv = (...rows: string[]) => ({ name: 'employees.csv', mimeType: 'text/csv', buffer: Buffer.from([header, ...rows].join('\n')) })

  await signInAs(page, 'HrAdmin', 'US')
  await openNav(page, 'People')
  await page.getByRole('button', { name: 'Import' }).click()

  const template = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download template' }).click()
  expect((await template).suggestedFilename()).toBe('employee-import-template.xlsx')

  await page.getByLabel('Spreadsheet').setInputFiles(csv(row(a, 'ENG'), row(b, 'NOPE', a)))
  await page.getByRole('button', { name: 'Import' }).click()
  await expect(page.getByText('Nothing was imported.')).toBeVisible()
  await expect(page.getByRole('table', { name: 'Import problems' }).getByRole('row', { name: /Department/ })).toContainText('NOPE')
  const report = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download error report' }).click()
  expect((await report).suggestedFilename()).toBe('employee-import-errors.xlsx')

  await page.getByLabel('Spreadsheet').setInputFiles(csv(row(a, 'ENG'), row(b, 'FIN', a)))
  await page.getByRole('button', { name: 'Import' }).click()
  await expect(page.getByText('Imported 2 employees.')).toBeVisible()

  await page.getByRole('link', { name: 'View them in People' }).click()
  await page.getByRole('searchbox', { name: 'Search people' }).fill(b)
  await expect(rowFor(page.getByRole('table', { name: 'People' }), `Imported Person ${b}`)).toContainText(`Imported Person ${a}`)
})

test('EMP-08: directory filters by department and status', async ({ page }) => {
  await signInAs(page, 'HrAdmin', 'PK')
  await openNav(page, 'People')
  const table = page.getByRole('table', { name: 'People' })

  await choose(page, 'Department', 'Finance')
  await choose(page, 'Country', 'Pakistan')
  await expect(rowFor(table, 'Finance Analyst (PK)')).toHaveCount(1)
  await expect(rowFor(table, devName('Employee', 'PK'))).toHaveCount(0)

  await choose(page, 'Department', 'All departments')
  await choose(page, 'Status', 'Probation')
  await expect(rowFor(table, 'Junior Engineer (PK)')).toHaveCount(1)
  await expect(rowFor(table, 'Finance Analyst (PK)')).toHaveCount(0)
})

test('NFR-02: employees and managers do not get Add employee or Import', async ({ page }) => {
  await signInAs(page, 'LineManager', 'PK')
  await openNav(page, 'People')

  await expect(page.getByRole('table', { name: 'People' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Add employee' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Import' })).toHaveCount(0)
})

test('Scheduled changes: a future promotion shows as upcoming, can be edited and cancelled', async ({ page, request }) => {
  const employee = await createEmployee(request, 'PK')
  await signInAs(page, 'HrAdmin', 'PK')
  await openProfile(page, employee.fullName)

  await page.getByRole('button', { name: 'Change job' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Designation').fill('Lead QA Engineer')
  await dialog.getByLabel('Effective date').fill('2030-01-15')
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(dialog).toHaveCount(0)

  const upcoming = page.getByLabel('Upcoming changes')
  await expect(upcoming).toContainText('Promotion on 15 Jan 2030')
  await expect(upcoming).toContainText('Lead QA Engineer')
  // Not applied yet: the record keeps the current designation.
  await expect(page.getByText(`QA Engineer · Engineering · ${employee.employeeNumber}`)).toBeVisible()

  await upcoming.getByRole('button', { name: 'Edit' }).click()
  await page.getByRole('dialog').getByLabel('Designation').fill('Principal QA Engineer')
  await page.getByRole('dialog').getByLabel('Effective date').fill('2030-02-01')
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(upcoming).toContainText('Promotion on 1 Feb 2030')
  await expect(upcoming).toContainText('Principal QA Engineer')

  await upcoming.getByRole('button', { name: 'Cancel change' }).click()
  await expect(upcoming).toHaveCount(0)
})

test('Scheduled changes: a future manager change is scheduled; a reporting loop is refused', async ({ page, request }) => {
  const manager = await createEmployee(request, 'PK')
  const report = await createEmployee(request, 'PK', { managerId: manager.id })
  await signInAs(page, 'HrAdmin', 'PK')
  await openProfile(page, report.fullName)

  await page.getByRole('button', { name: 'Change manager' }).click()
  await page.getByRole('combobox', { name: 'Reporting manager' }).click()
  await page.getByRole('option', { name: 'No manager' }).click()
  await page.getByRole('dialog').getByLabel('Effective date').fill('2030-03-01')
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByLabel('Upcoming changes')).toContainText('Manager: no manager')

  const loop = await request.post(`${apiUrl}/api/employees/${manager.id}/manager`, {
    headers: asUser('HrAdmin', 'PK'),
    data: { managerId: report.id, effectiveDate: '2030-03-01', reason: null },
  })
  expect(loop.status()).toBe(400)
})
