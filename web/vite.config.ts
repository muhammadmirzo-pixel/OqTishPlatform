import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Dev server proxies API and health calls to the locally running API (dotnet run → :5080),
// mirroring the nginx routing used in containers.
const apiTarget = process.env.OQTISH_API_DEV_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
