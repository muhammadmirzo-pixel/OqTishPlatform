import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ServerStatus } from './ServerStatus'

describe('ServerStatus', () => {
  it('shows ready when the readiness probe succeeds', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('Healthy', { status: 200 }))
    render(<ServerStatus fetchImpl={fetchMock} />)
    expect(await screen.findByText('API ✓')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/health/ready', expect.anything())
  })

  it('shows unavailable on 503 and on network failure', async () => {
    const { unmount } = render(<ServerStatus fetchImpl={vi.fn().mockResolvedValue(new Response('Unhealthy', { status: 503 }))} />)
    expect(await screen.findByText('API ✕')).toBeInTheDocument()
    unmount()
    render(<ServerStatus fetchImpl={vi.fn().mockRejectedValue(new TypeError('offline'))} />)
    expect(await screen.findByText('API ✕')).toBeInTheDocument()
  })
})
