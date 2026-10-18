import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

// Lazily loaded route chunks are transformed on first use; allow for a slow CI machine.
configure({ asyncUtilTimeout: 5000 })

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  try {
    localStorage.clear()
  } catch {
    // ignore
  }
})
