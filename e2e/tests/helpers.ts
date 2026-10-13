import { expect, type Locator, type Page } from '@playwright/test'

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

/** A short suffix that keeps names unique across runs and retries. */
export function unique(): string {
  return Math.random().toString(36).slice(2, 7).toUpperCase()
}
