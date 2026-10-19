import react from '@vitejs/plugin-react'
import { searchForWorkspaceRoot } from 'vite'
import { configDefaults, defineConfig } from 'vitest/config'

// Dev server proxies API and health calls to the locally running API (dotnet run → :5080),
// mirroring the nginx routing used in containers.
const apiTarget = process.env.OQTISH_API_DEV_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    // Tests read docs/API.md (?raw) to check that every documented error code is localised.
    fs: process.env.VITEST ? { allow: [searchForWorkspaceRoot(process.cwd()), '../docs'] } : undefined,
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Playwright suites (npm run e2e) live in e2e/.
    exclude: [...configDefaults.exclude, 'e2e/**'],
  },
})
