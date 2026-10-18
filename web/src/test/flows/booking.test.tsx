import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { appointment, clinicDetail, patientMe } from '../fixtures'
import { meHandler, mockFetch, problem, renderApp, type Handler } from '../harness'

const slots = (times: [string, string][]) => ({ date: '2026-10-19', timeZone: 'Asia/Tashkent', durationMinutes: 30, slots: times.map(([localTime, startsAt]) => ({ localTime, startsAt })) })

function base(extra: Handler[] = []): Handler[] {
  return [
    meHandler(patientMe),
    ['GET', '/public/clinics/c1', { body: clinicDetail }],
    ['GET', '/public/branches/b1/availability', { body: slots([['10:00', '2026-10-19T05:00:00Z'], ['10:30', '2026-10-19T05:30:00Z']]) }],
    ['GET', '/me/appointments/a1', { body: appointment({ startsAt: '2026-10-19T05:00:00Z', endsAt: '2026-10-19T05:30:00Z' }) }],
    ...extra,
  ]
}

async function walkToSummary(user: ReturnType<typeof userEvent.setup>) {
  // The only branch is pre-selected, so the wizard opens on the service step.
  await user.click(await screen.findByRole('button', { name: /Konsultatsiya/ }))
  await user.click(await screen.findByRole('button', { name: '10:00' }))
  const name = await screen.findByLabelText('Full name')
  expect(name).toHaveValue('Test Bemor')
  expect(screen.getByLabelText('Phone number')).toHaveValue('+998901234567')
  await user.click(screen.getByRole('button', { name: 'Next' }))
  await screen.findByRole('heading', { name: 'Review and send' })
}

describe('booking wizard', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true, now: new Date('2026-10-19T04:00:00Z') })
  })

  it('books a slot: branch → service → time → contact → summary → appointment page', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const fetchMock = mockFetch(base([['POST', '/appointments', { status: 201, body: appointment() }]]))
    const { router } = renderApp('/book/c1', fetchMock)

    await walkToSummary(user)
    expect(screen.getByText(/The clinic assigns the doctor/)).toBeInTheDocument()
    expect(screen.getByText(/must reply within 30 minutes/)).toBeInTheDocument()
    expect(screen.getByText('100,000 UZS')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Send request' }))
    const post = fetchMock.find('POST', '/appointments')[0]!
    expect(post.body).toEqual({ branchId: 'b1', serviceId: 's1', startsAt: '2026-10-19T05:00:00Z', contactName: 'Test Bemor', contactPhone: '+998901234567' })
    expect(post.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(post.headers['X-OqTish-Client']).toBe('web')

    await waitFor(() => expect(router.state.location.pathname).toBe('/me/appointments/a1'))
    expect(await screen.findByText(/Your request was sent/)).toBeInTheDocument()
  })

  it('on 409 booking.slot_unavailable refreshes the slots and asks for another time', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    let availabilityCalls = 0
    const fetchMock = mockFetch(
      base([
        [
          'GET',
          '/public/branches/b1/availability',
          () => {
            availabilityCalls++
            return { body: availabilityCalls === 1 ? slots([['10:00', '2026-10-19T05:00:00Z'], ['10:30', '2026-10-19T05:30:00Z']]) : slots([['10:30', '2026-10-19T05:30:00Z']]) }
          },
        ],
        ['POST', '/appointments', (call) => ((call.body as { startsAt: string }).startsAt === '2026-10-19T05:00:00Z' ? () => problem(409, 'booking.slot_unavailable') : { status: 201, body: appointment() })],
      ]),
    )
    const { router } = renderApp('/book/c1', fetchMock)
    await walkToSummary(user)
    await user.click(screen.getByRole('button', { name: 'Send request' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The chosen time was just taken')
    await waitFor(() => expect(availabilityCalls).toBe(2))
    expect(screen.queryByRole('button', { name: '10:00' })).toBeNull()
    expect(screen.getByRole('heading', { name: 'Date and time' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '10:30' }))
    await user.click(await screen.findByRole('button', { name: 'Next' }))
    await user.click(await screen.findByRole('button', { name: 'Send request' }))
    const posts = fetchMock.find('POST', '/appointments')
    expect(posts).toHaveLength(2)
    expect(posts[1]!.headers['Idempotency-Key']).not.toBe(posts[0]!.headers['Idempotency-Key'])
    await waitFor(() => expect(router.state.location.pathname).toBe('/me/appointments/a1'))
  })

  it('shows 422 rule violations inline in the summary', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const fetchMock = mockFetch(base([['POST', '/appointments', () => problem(422, 'booking.too_many_pending')]]))
    renderApp('/book/c1', fetchMock)
    await walkToSummary(user)
    await user.click(screen.getByRole('button', { name: 'Send request' }))
    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText(/already have 3 requests/)).toBeInTheDocument()
  })

  it('asks anonymous visitors to sign in at the contact step, keeping the selection in ?next', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const fetchMock = mockFetch([...base(), meHandler(null)])
    renderApp('/book/c1', fetchMock)
    await user.click(await screen.findByRole('button', { name: /Konsultatsiya/ }))
    await user.click(await screen.findByRole('button', { name: '10:00' }))
    await screen.findByText(/Sign in with your phone number/)
    const link = screen.getAllByRole('link', { name: 'Sign in' }).find((l) => l.getAttribute('href')?.includes('next='))!
    const next = decodeURIComponent(new URL(link.getAttribute('href')!, 'http://x').searchParams.get('next')!)
    expect(next).toContain('/book/c1?')
    expect(next).toContain('service=s1')
    expect(new URLSearchParams(next.split('?')[1]).get('start')).toBe('2026-10-19T05:00:00Z')
  })
})
