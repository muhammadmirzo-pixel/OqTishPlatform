// Approximate mobile Web Vitals of the production build (not Lighthouse): run `npm run build`,
// `npx vite preview --port 4173`, then `E2E_PERF=1 E2E_BASE_URL=http://localhost:4173 npx playwright test perf --project=mobile-360`.
// Chrome is throttled like Lighthouse's mobile preset (4× CPU, ~1.6 Mbit/s, 150 ms RTT); the API is
// canned, so figures cover the front end only.
import { expect, test } from '@playwright/test'
import { mockApi, type Persona } from './support/mockApi'

test.skip(!process.env.E2E_PERF, 'set E2E_PERF=1 to measure')

const pages: { name: string; path: string; persona: Persona }[] = [
  { name: 'landing', path: '/', persona: 'guest' },
  { name: 'catalogue', path: '/clinics', persona: 'guest' },
  { name: 'clinic page', path: '/clinics/c1', persona: 'guest' },
  { name: 'clinic dashboard', path: '/clinic/c1', persona: 'owner' },
]

for (const p of pages) {
  test(`web vitals: ${p.name}`, async ({ page }, testInfo) => {
    await mockApi(page, p.persona)
    const cdp = await page.context().newCDPSession(page)
    await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 })
    await cdp.send('Network.enable')
    await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 150, downloadThroughput: (1.6 * 1024 * 1024) / 8, uploadThroughput: (750 * 1024) / 8 })
    await page.addInitScript(() => {
      const w = window as unknown as { __lcp: number; __cls: number }
      w.__lcp = 0
      w.__cls = 0
      new PerformanceObserver((list) => {
        for (const e of list.getEntries()) w.__lcp = e.startTime
      }).observe({ type: 'largest-contentful-paint', buffered: true })
      new PerformanceObserver((list) => {
        for (const e of list.getEntries() as (PerformanceEntry & { value: number; hadRecentInput: boolean })[]) if (!e.hadRecentInput) w.__cls += e.value
      }).observe({ type: 'layout-shift', buffered: true })
    })
    await page.goto(p.path, { waitUntil: 'load' })
    await expect(page.locator('h1').first()).toBeVisible()
    await page.waitForTimeout(1500)
    const m = await page.evaluate(() => {
      const w = window as unknown as { __lcp: number; __cls: number }
      const js = performance
        .getEntriesByType('resource')
        .filter((r) => r.name.endsWith('.js'))
        .reduce((n, r) => n + (r as PerformanceResourceTiming).transferSize, 0)
      return { lcpMs: Math.round(w.__lcp), cls: Number(w.__cls.toFixed(3)), jsTransferKb: Math.round(js / 1024) }
    })
    testInfo.annotations.push({ type: 'vitals', description: JSON.stringify(m) })
    console.log(`${p.name}: ${JSON.stringify(m)}`)
  })
}
