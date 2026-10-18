import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { createApi } from '../../api'
import { createApiClient } from '../../api/client'
import { ApiContext } from '../../api/context'
import type { BranchDto } from '../../api/types'
import { ClinicContext, makeClinicContext } from '../../features/clinic/clinicContext'
import { RecordPaymentForm } from '../../features/finance/RecordPaymentForm'
import { I18nProvider } from '../../i18n/I18n'
import { appointment, membership, staffMe } from '../fixtures'
import { meHandler, mockFetch, problem, renderApp, type FetchMock } from '../harness'

const branch: BranchDto = { id: 'b1', name: 'Markaziy filial', district: 'Yunusobod', address: 'Test 1', latitude: null, longitude: null, phone: '+998711234567', timeZone: 'Asia/Tashkent', isActive: true, hours: [] }
const receptionist = ['appointment.view', 'appointment.triage', 'appointment.cancel', 'appointment.outcome', 'appointment.create_walkin', 'payment.record', 'schedule.manage']
const page = (items: unknown[]) => ({ body: { items, page: 1, pageSize: 50, totalCount: items.length } })

describe('clinic requests', () => {
  it('confirms a Pending request with the chosen eligible doctor', async () => {
    const user = userEvent.setup()
    let confirmed = false
    const fetchMock = mockFetch([
      meHandler(staffMe(receptionist)),
      ['GET', '/clinics/c1/branches', { body: [branch] }],
      ['GET', '/clinics/c1/appointments', () => page(confirmed ? [] : [appointment()])],
      ['GET', '/clinics/c1/appointments/a1/eligible-doctors', { body: [{ id: 'd1', name: 'Dr Alfa' }, { id: 'd2', name: 'Dr Beta' }] }],
      [
        'POST',
        '/clinics/c1/appointments/a1/confirm',
        () => {
          confirmed = true
          return { status: 204 }
        },
      ],
    ])
    renderApp('/clinic/c1/requests', fetchMock)

    const select = await screen.findByLabelText('Doctor')
    await waitFor(() => expect(select).toBeEnabled())
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    expect(screen.getAllByText('Choose a doctor').length).toBeGreaterThan(0)
    expect(fetchMock.find('POST', '/clinics/c1/appointments/a1/confirm')).toHaveLength(0)

    await user.selectOptions(select, 'd2')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    const post = fetchMock.find('POST', '/clinics/c1/appointments/a1/confirm')[0]!
    expect(post.body).toEqual({ doctorId: 'd2' })
    expect(post.headers['X-OqTish-Client']).toBe('web')
    expect(await screen.findByText('Request confirmed. The patient will be notified.')).toBeInTheDocument()
    expect(await screen.findByText('No requests are waiting for a reply.')).toBeInTheDocument()
    expect(fetchMock.find('GET', '/clinics/c1/appointments')[0]!.search.get('status')).toBe('Pending')
  })

  it('reports booking.expired and refreshes the list', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(staffMe(receptionist)),
      ['GET', '/clinics/c1/branches', { body: [branch] }],
      ['GET', '/clinics/c1/appointments', page([appointment()])],
      ['GET', '/clinics/c1/appointments/a1/eligible-doctors', { body: [{ id: 'd1', name: 'Dr Alfa' }] }],
      ['POST', '/clinics/c1/appointments/a1/confirm', () => problem(409, 'booking.expired')],
    ])
    renderApp('/clinic/c1/requests', fetchMock)
    // A single eligible doctor is pre-selected.
    await waitFor(() => expect(screen.getByLabelText('Doctor')).toHaveValue('d1'))
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    expect(await screen.findByText('The deadline to answer this request has passed.')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock.find('GET', '/clinics/c1/appointments').length).toBeGreaterThanOrEqual(2))
  })
})

describe('clinic console menu', () => {
  it('is filtered by the membership permissions from /api/me, not by role', async () => {
    renderApp('/clinic/c1', mockFetch([meHandler(staffMe(receptionist)), ['GET', '/clinics/c1/branches', { body: [branch] }], ['GET', '/clinics/c1/dashboard', { body: { date: '2026-10-19', pending: 2, confirmed: 5, completed: 1, noShow: 0 } }]]))
    const nav = await screen.findByRole('navigation', { name: 'Menu' })
    for (const name of ['Dashboard', 'Requests', 'Appointments', 'Day calendar', 'New booking', 'Doctors', 'Payments']) expect(nav).toHaveTextContent(name)
    for (const name of ['Reports', 'Staff', 'Settlements', 'Audit log', 'Services & prices']) expect(nav).not.toHaveTextContent(name)
    expect(await screen.findByText('5')).toBeInTheDocument()
  })
})

describe('record payment form', () => {
  function renderForm(fetchMock: FetchMock) {
    const api = createApi(createApiClient('/api', fetchMock as unknown as typeof fetch))
    const ctx = makeClinicContext(membership(receptionist), [branch], () => undefined)
    return render(
      <I18nProvider initial="en">
        <ApiContext.Provider value={api}>
          <ClinicContext.Provider value={ctx}>
            <RecordPaymentForm clinicId="c1" appointmentId="a1" onRecorded={() => undefined} />
          </ClinicContext.Provider>
        </ApiContext.Provider>
      </I18nProvider>,
    )
  }

  it('validates amount (decimal string, > 0, ≤ 2 decimals) and method before sending', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([['POST', '/clinics/c1/appointments/a1/payments', { status: 201, body: { id: 'p1', amount: { amount: '150000.00', currency: 'UZS' } } }]])
    renderForm(fetchMock)
    const submit = screen.getByRole('button', { name: 'Record' })
    const amount = screen.getByLabelText('Amount')

    await user.click(submit)
    expect(screen.getByText('Please fill in this field')).toBeInTheDocument()
    expect(screen.getAllByText('Choose a payment method').length).toBeGreaterThan(1)

    await user.type(amount, '12.345')
    await user.click(submit)
    expect(screen.getByText(/Enter a valid amount/)).toBeInTheDocument()

    await user.clear(amount)
    await user.type(amount, '0')
    await user.click(submit)
    expect(screen.getByText('The amount must be above 0')).toBeInTheDocument()
    expect(fetchMock.calls).toHaveLength(0)

    await user.clear(amount)
    await user.type(amount, '150 000')
    await user.selectOptions(screen.getByLabelText('Payment method'), 'Cash')
    await user.click(submit)
    const post = fetchMock.find('POST', '/clinics/c1/appointments/a1/payments')[0]!
    expect(post.body).toEqual({ amount: { amount: '150000.00', currency: 'UZS' }, method: 'Cash' })
    expect(post.headers['Idempotency-Key']).toBeTruthy()
    expect(await screen.findByText('Payment of 150,000 UZS recorded.')).toBeInTheDocument()
  })

  it('keeps the same Idempotency-Key when retrying after a network failure', async () => {
    const user = userEvent.setup()
    let attempt = 0
    const fetchMock = mockFetch([
      [
        'POST',
        '/clinics/c1/appointments/a1/payments',
        () => {
          attempt++
          if (attempt === 1) return () => Promise.reject(new TypeError('Failed to fetch'))
          return { status: 201, body: { id: 'p1', amount: { amount: '5000.00', currency: 'UZS' } } }
        },
      ],
    ])
    renderForm(fetchMock)
    await user.type(screen.getByLabelText('Amount'), '5000')
    await user.selectOptions(screen.getByLabelText('Payment method'), 'Card')
    await user.click(screen.getByRole('button', { name: 'Record' }))
    expect(await screen.findByText(/No connection to the server/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Record' }))
    await screen.findByText('Payment of 5,000 UZS recorded.')
    const [first, second] = fetchMock.find('POST', '/clinics/c1/appointments/a1/payments')
    expect(second!.headers['Idempotency-Key']).toBe(first!.headers['Idempotency-Key'])
  })
})
