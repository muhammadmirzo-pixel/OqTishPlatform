// UI checks on canned data (e2e/support/mockApi.ts): WCAG 2.2 AA scan with axe, no horizontal
// scrolling, and a full-page screenshot per viewport/theme (attached to the report). Runs without
// a backend; the real user journeys are in e2e/journeys.
import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { mockApi, type Persona } from './support/mockApi'

const screens: { name: string; path: string; persona: Persona }[] = [
  { name: 'landing', path: '/', persona: 'guest' },
  { name: 'catalogue', path: '/clinics', persona: 'guest' },
  { name: 'clinic page', path: '/clinics/c1', persona: 'guest' },
  { name: 'login', path: '/login', persona: 'guest' },
  { name: 'clinic dashboard', path: '/clinic/c1', persona: 'owner' },
  { name: 'request inbox', path: '/clinic/c1/requests', persona: 'owner' },
  { name: 'calendar', path: '/clinic/c1/calendar', persona: 'owner' },
  { name: 'admin dashboard', path: '/admin', persona: 'admin' },
  { name: 'admin clinics', path: '/admin/clinics', persona: 'admin' },
]

async function open(page: Page, path: string, persona: Persona) {
  await mockApi(page, persona)
  await page.addInitScript(() => window.localStorage.setItem('oqtish.locale', 'uz'))
  await page.goto(path)
  await expect(page.locator('h1').first()).toBeVisible()
  // Content, not placeholders: skeletons and spinners are gone.
  await expect(page.locator('.skeleton, .spinner')).toHaveCount(0)
}

for (const s of screens) {
  test.describe(s.name, () => {
    test('has no serious or critical WCAG 2.2 AA violations', async ({ page }) => {
      await open(page, s.path, s.persona)
      const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze()
      const serious = violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
      expect(serious.map((v) => `${v.id} (${v.nodes.length}): ${v.nodes[0]?.target.join(' ')}`)).toEqual([])
    })

    test('does not scroll horizontally and renders', async ({ page }, testInfo) => {
      await open(page, s.path, s.persona)
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      expect(overflow).toBeLessThanOrEqual(0)
      await page.screenshot({ path: testInfo.outputPath(`${s.name.replace(/\s+/g, '-')}.png`), fullPage: true })
    })
  })
}
