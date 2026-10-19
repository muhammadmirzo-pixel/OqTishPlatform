// Dev helper: node scripts/screenshot.mjs <url> <out.png> <width> <height> [light|dark]
import { chromium } from '@playwright/test'
const [,, url, out, w, h, scheme] = process.argv
const b = await chromium.launch()
const p = await b.newPage({ viewport: { width: +w, height: +h }, colorScheme: scheme || 'light' })
await p.goto(url, { waitUntil: 'load' })
await p.waitForTimeout(2500)
await p.screenshot({ path: out, fullPage: true })
await b.close()
