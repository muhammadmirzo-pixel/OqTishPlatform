import { describe, expect, it, vi } from 'vitest'
import { loadConfig } from './config'

describe('loadConfig', () => {
  it('uses apiBaseUrl from /config.json', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ apiBaseUrl: 'https://api.example.test' }))
    expect(await loadConfig(fetchMock)).toEqual({ apiBaseUrl: 'https://api.example.test' })
  })

  it('falls back to /api when config.json is missing or invalid', async () => {
    expect(await loadConfig(vi.fn().mockResolvedValue(new Response('', { status: 404 })))).toEqual({ apiBaseUrl: '/api' })
    expect(await loadConfig(vi.fn().mockResolvedValue(Response.json({ apiBaseUrl: 42 })))).toEqual({ apiBaseUrl: '/api' })
    expect(await loadConfig(vi.fn().mockRejectedValue(new TypeError('offline')))).toEqual({ apiBaseUrl: '/api' })
  })
})
