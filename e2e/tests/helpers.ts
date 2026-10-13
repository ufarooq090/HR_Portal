import { type APIRequestContext, expect, type Locator, type Page } from '@playwright/test'

export const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5295'

/** Display name of a seeded dev user, e.g. devName('HrAdmin', 'PK') = "Dev HrAdmin (PK)". */
export function devName(role: string, country: string): string {
  return `Dev ${role} (${country})`
}

export function devId(role: string, country: string): string {
  return `dev-${country}-${role}`.toLowerCase()
}

export async function signInAs(page: Page, role: string, country: string): Promise<void> {
  const name = devName(role, country)
  await page.goto('/')
  await page.getByRole('button', { name: `Sign in as ${name}` }).click()
  await expect(page.getByRole('heading', { level: 1, name: `Welcome, ${name}` })).toBeVisible()
}

export async function switchUser(page: Page, currentName: string): Promise<void> {
  await page.getByRole('button', { name: currentName }).click()
  await page.getByRole('menuitem', { name: 'Switch user' }).click()
  await expect(page.getByRole('heading', { name: 'HR Portal — development sign-in' })).toBeVisible()
}

export async function openNav(page: Page, name: string): Promise<void> {
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name, exact: true }).click()
}

export function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/** A table row whose first cell is exactly `name` (names also appear in other rows' Manager column). */
export function rowFor(table: Locator, name: string): Locator {
  return table.getByRole('row', { name: new RegExp(`^${escapeRegExp(name)} `) })
}

export function asUser(role: string, country: string): Record<string, string> {
  return { 'X-Dev-User': devId(role, country) }
}

/**
 * Creates a throwaway employee through the API (as the HR Admin of that country), with no manager, so the seeded
 * reporting lines that other specs count stay untouched.
 */
export async function createEmployee(
  request: APIRequestContext,
  country: 'PK' | 'US',
  overrides: Record<string, unknown> = {},
): Promise<{ id: string; employeeNumber: string; fullName: string }> {
  const headers = asUser('HrAdmin', country)
  const get = async <T>(path: string) => (await (await request.get(`${apiUrl}${path}`, { headers })).json()) as T
  const entities = await get<{ id: string; countryCode: string; name: string }[]>('/api/organisation/legal-entities')
  const offices = await get<{ id: string; countryCode: string; name: string }[]>('/api/organisation/locations')
  const departments = await get<{ id: string; code: string }[]>('/api/organisation/departments')

  const number = `E2E-${unique()}${unique()}`
  const body = {
    givenName: 'E2E',
    familyName: `Person ${number}`,
    employeeNumber: number,
    workEmail: `${number.toLowerCase()}@e2e.test`,
    employmentType: 'Permanent',
    designation: 'QA Engineer',
    legalEntityId: entities.find((e) => e.countryCode === country && e.name.startsWith('HR Portal Dev'))!.id,
    officeLocationId: offices.find((o) => o.name === (country === 'PK' ? 'Lahore Office' : 'US Office'))!.id,
    departmentId: departments.find((d) => d.code === 'ENG')!.id,
    gradeId: null,
    managerId: null,
    joiningDate: '2025-01-06',
    personal: null,
    ...overrides,
  }
  const response = await request.post(`${apiUrl}/api/employees`, { headers, data: body })
  expect(response.status()).toBe(201)
  const created = (await response.json()) as { id: string }
  return { id: created.id, employeeNumber: number, fullName: `${body.givenName} ${body.familyName}` }
}

/** A short suffix that keeps names unique across runs and retries. */
export function unique(): string {
  return Math.random().toString(36).slice(2, 7).toUpperCase()
}
