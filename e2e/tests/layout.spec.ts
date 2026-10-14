import { expect, type Page, test } from '@playwright/test'
import { apiUrl, asUser, signInAs } from './helpers'

// Real scrollbars (Playwright hides them by default): a vertical scrollbar takes ~15px of the width on Windows, which is
// what pushes a page that only just fits into scrolling sideways.
test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] } })

// Every page in the HR Admin's menu, plus the forms and the user's own profile.
const pages = [
  '/',
  '/people',
  '/people/new',
  '/people/import',
  '/requisitions',
  '/requisitions/new',
  '/approvals',
  '/candidates',
  '/interviews',
  '/resume-bank',
  '/resume-bank/import',
  '/templates',
  '/templates/new',
  '/settings/countries',
  '/settings/legal-entities',
  '/settings/locations',
  '/settings/departments',
  '/settings/grades',
  '/settings/approval-chains',
  '/settings/rejection-reasons',
  '/settings/hr-access',
]

/** Elements that stick out past the right edge of the viewport, outside any container that scrolls them. */
async function overflow(page: Page): Promise<{ scrollWidth: number; clientWidth: number; culprits: string[] }> {
  return page.evaluate(() => {
    const { scrollWidth, clientWidth } = document.documentElement
    const insideScroller = (element: Element) => {
      for (let parent = element.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
        if (/auto|scroll|hidden|clip/.test(getComputedStyle(parent).overflowX)) return true
      }
      return false
    }
    const culprits = [...document.body.querySelectorAll('*')]
      .filter((e) => e.getBoundingClientRect().right > clientWidth + 1 && !insideScroller(e))
      .slice(0, 5)
      .map((e) => `<${e.tagName.toLowerCase()} class="${String(e.getAttribute('class') ?? '').slice(0, 60)}">`)
    return { scrollWidth, clientWidth, culprits }
  })
}

for (const width of [1366, 1920]) {
  test(`NFR layout: no page scrolls sideways at ${width}px`, async ({ page, request }) => {
    test.slow()
    const me = (await (await request.get(`${apiUrl}/api/me`, { headers: asUser('HrAdmin', 'PK') })).json()) as { employeeId: string }

    await page.setViewportSize({ width, height: 900 })
    await signInAs(page, 'HrAdmin', 'PK')
    const failures: string[] = []
    for (const path of [...pages, `/people/${me.employeeId}`]) {
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await expect(page.locator('.fui-Spinner')).toHaveCount(0)
      const result = await overflow(page)
      if (result.scrollWidth > result.clientWidth) {
        failures.push(`${path}: ${result.scrollWidth}px wide in a ${result.clientWidth}px viewport, caused by ${result.culprits.join(', ')}`)
      }
    }
    expect(failures, 'Pages wider than the viewport (wide tables must scroll inside their own container)').toEqual([])
  })
}
