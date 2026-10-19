import { defineConfig, devices } from '@playwright/test'

// UI suite: canned API, runs anywhere (`npm run e2e`). The dev server is started unless one is
// already listening on :5199. Projects cover the responsive matrix (360/768/1280), dark theme and
// reduced motion (FRONTEND_PHASE_A.md §10).
export default defineConfig({
  testDir: './e2e',
  testIgnore: process.env.E2E_BACKEND ? [] : ['journeys/**'],
  fullyParallel: true,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5199',
    ...devices['Desktop Chrome'],
    locale: 'uz-UZ',
    timezoneId: 'Asia/Tashkent',
  },
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : { command: 'npx vite --port 5199 --strictPort', url: 'http://localhost:5199', reuseExistingServer: true, timeout: 60_000 },
  projects: [
    { name: 'mobile-360', use: { viewport: { width: 360, height: 740 }, isMobile: true, hasTouch: true } },
    { name: 'tablet-768', use: { viewport: { width: 768, height: 1024 } } },
    { name: 'desktop-1280', use: { viewport: { width: 1280, height: 800 } } },
    { name: 'desktop-dark', use: { viewport: { width: 1280, height: 800 }, colorScheme: 'dark' } },
    { name: 'mobile-reduced-motion', use: { viewport: { width: 360, height: 740 }, isMobile: true, reducedMotion: 'reduce' } },
  ],
})
