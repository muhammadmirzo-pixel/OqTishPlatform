import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { appointment, patientMe } from '../fixtures'
import { meHandler, mockFetch, problem, renderApp } from '../harness'

describe('my appointments', () => {
  it('shows the server deadline countdown (display only) and cancels a Pending request', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, now: new Date('2026-10-19T04:00:00Z') })
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    let cancelled = false
    const fetchMock = mockFetch([
      meHandler(patientMe),
      ['GET', '/me/appointments/a1', () => ({ body: cancelled ? appointment({ status: 'CancelledByPatient', canCancel: false, expiresAt: null }) : appointment() })],
      [
        'POST',
        '/me/appointments/a1/cancel',
        () => {
          cancelled = true
          return { status: 204 }
        },
      ],
    ])
    renderApp('/me/appointments/a1', fetchMock)

    expect(await screen.findByText(/^Waiting for the clinic: (30:00|29:5\d)$/)).toBeInTheDocument()
    // Jump the clock, then let the 1 s display tick run once.
    const tick = async (iso: string) => {
      vi.setSystemTime(new Date(iso))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(1000)
      })
    }
    await tick('2026-10-19T04:05:00Z')
    expect(screen.getByText('Waiting for the clinic: 24:59')).toBeInTheDocument()
    await tick('2026-10-19T04:31:00Z')
    // Past the deadline the UI only says so; the status itself comes from the server.
    expect(screen.getByText(/The reply deadline has passed/)).toBeInTheDocument()
    expect(screen.getAllByText('Awaiting reply').length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Cancel appointment' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancel this appointment?' })
    expect(dialog).toHaveTextContent('released for others')
    await user.click(screen.getByRole('button', { name: 'Yes, cancel' }))

    const post = fetchMock.find('POST', '/me/appointments/a1/cancel')[0]!
    expect(post.headers['X-OqTish-Client']).toBe('web')
    expect(await screen.findAllByText('Cancelled by patient')).not.toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Cancel appointment' })).toBeNull()
  })

  it('hides cancel when the server says canCancel=false and explains the 2 h cutoff', async () => {
    renderApp('/me/appointments/a1', mockFetch([meHandler(patientMe), ['GET', '/me/appointments/a1', { body: appointment({ status: 'Confirmed', expiresAt: null, canCancel: false, doctor: { id: 'd1', name: 'Dr Test' } }) }]]))
    expect(await screen.findByText(/Less than 2 hours before the start/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel appointment' })).toBeNull()
    expect(screen.getByText('Dr Test')).toBeInTheDocument()
  })

  it('shows booking.cancel_too_late from the server inside the dialog', async () => {
    const user = userEvent.setup()
    renderApp(
      '/me/appointments/a1',
      mockFetch([
        meHandler(patientMe),
        ['GET', '/me/appointments/a1', { body: appointment({ status: 'Confirmed', expiresAt: null }) }],
        ['POST', '/me/appointments/a1/cancel', () => problem(422, 'booking.cancel_too_late')],
      ]),
    )
    await user.click(await screen.findByRole('button', { name: 'Cancel appointment' }))
    await user.click(await screen.findByRole('button', { name: 'Yes, cancel' }))
    await waitFor(() => expect(screen.getByRole('dialog')).toHaveTextContent('cannot be cancelled less than 2 hours'))
  })
})
