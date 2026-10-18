import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { AdminClinicDto, ClinicVerification } from '../../api/types'
import { patientMe } from '../fixtures'
import { meHandler, mockFetch, problem, renderApp } from '../harness'

const admin = { ...patientMe, id: 'u-admin', patient: null, totpEnabled: true, platformPermissions: ['platform.clinic.verify'] }
const clinic = (status: ClinicVerification, statusReason: string | null = null): AdminClinicDto => ({
  id: 'k1',
  name: 'Yangi Klinika',
  legalName: 'Yangi Klinika MChJ',
  phone: '+998711112233',
  verificationStatus: status,
  isSuspended: status === 'Suspended',
  statusReason,
  branchCount: 1,
  createdAt: '2026-10-18T10:00:00Z',
})

describe('platform admin', () => {
  it('approves a Pending clinic and refreshes the list', async () => {
    const user = userEvent.setup()
    let approved = false
    const fetchMock = mockFetch([
      meHandler(admin),
      ['GET', '/admin/clinics', () => ({ body: { items: [clinic(approved ? 'Approved' : 'Pending')], page: 1, pageSize: 20, totalCount: 1 } })],
      [
        'POST',
        '/admin/clinics/k1/approve',
        () => {
          approved = true
          return { body: clinic('Approved') }
        },
      ],
    ])
    renderApp('/admin/clinics', fetchMock)
    const row = (await screen.findByText('Yangi Klinika')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Approve' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Approve' }))
    expect(fetchMock.find('POST', '/admin/clinics/k1/approve')[0]!.headers['X-OqTish-Client']).toBe('web')
    expect(await screen.findByText('Done.')).toBeInTheDocument()
    await waitFor(() => expect(within(screen.getByText('Yangi Klinika').closest('tr')!).queryByRole('button', { name: 'Approve' })).toBeNull())
    // Suspend needs platform.account.suspend, which this admin does not have.
    expect(screen.queryByRole('button', { name: 'Suspend' })).toBeNull()
  })

  it('shows the reject reason and allows re-approving a Rejected clinic (but not rejecting again)', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(admin),
      ['GET', '/admin/clinics', { body: { items: [clinic('Rejected', 'Hujjatlar to‘liq emas')], page: 1, pageSize: 20, totalCount: 1 } }],
      ['POST', '/admin/clinics/k1/approve', () => problem(409, 'admin.invalid_status_transition')],
    ])
    renderApp('/admin/clinics', fetchMock)
    const row = (await screen.findByText('Yangi Klinika')).closest('tr')!
    expect(within(row).getByText('Reason: Hujjatlar to‘liq emas')).toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: 'Reject' })).toBeNull()
    await user.click(within(row).getByRole('button', { name: 'Approve' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Approve' }))
    expect(await within(dialog).findByText('This action is not possible in the current state. Refresh the list.')).toBeInTheDocument()
  })

  it('does not offer to suspend the signed-in admin themself', async () => {
    const suspender = { ...admin, platformPermissions: ['platform.account.suspend'] }
    const user = (id: string, displayName: string) => ({ id, displayName, phoneMasked: null, email: null, status: 'Active', statusReason: null, createdAt: '2026-10-18T10:00:00Z' })
    renderApp('/admin/users', mockFetch([meHandler(suspender), ['GET', '/admin/users', { body: { items: [user('u-admin', 'Men'), user('u-x', 'Boshqa')], page: 1, pageSize: 20, totalCount: 2 } }]]))
    const own = (await screen.findByText('Men')).closest('tr')!
    expect(within(own).queryByRole('button', { name: 'Suspend' })).toBeNull()
    expect(within(screen.getByText('Boshqa').closest('tr')!).getByRole('button', { name: 'Suspend' })).toBeInTheDocument()
  })

  it('shows only menu entries for granted platform permissions', async () => {
    renderApp(
      '/admin',
      mockFetch([meHandler(admin), ['GET', '/admin/dashboard', { body: { clinicsByStatus: { Pending: 1 }, appointmentsToday: 3, openReports: 0, activeUsers: 42, suspendedUsers: 2 } }]]),
    )
    const nav = await screen.findByRole('navigation', { name: 'Menu' })
    expect(nav).toHaveTextContent('Clinics')
    expect(nav).not.toHaveTextContent('Settlements')
    expect(nav).not.toHaveTextContent('Patient data access')
    expect(await screen.findByText('42')).toBeInTheDocument()
    expect(screen.getByText('Suspended users')).toBeInTheDocument()
  })

  it('sends the break-glass reason in X-Access-Reason (percent-encoded)', async () => {
    const user = userEvent.setup()
    const reader = { ...admin, platformPermissions: ['platform.tenant-data.read'] }
    const fetchMock = mockFetch([meHandler(reader), ['GET', '/admin/clinics/k1/appointments', { body: { items: [], page: 1, pageSize: 20, totalCount: 0 } }]])
    renderApp('/admin/tenant-data', fetchMock)
    await user.type(await screen.findByLabelText('Clinic ID'), 'k1')
    await user.type(screen.getByLabelText('Reason for access'), 'Shikoyat №42 bo‘yicha tekshiruv')
    await user.click(screen.getByRole('button', { name: 'Open data' }))
    await waitFor(() => expect(fetchMock.find('GET', '/admin/clinics/k1/appointments')).toHaveLength(1))
    const header = fetchMock.find('GET', '/admin/clinics/k1/appointments')[0]!.headers['X-Access-Reason']!
    expect(decodeURIComponent(header)).toBe('Shikoyat №42 bo‘yicha tekshiruv')
    expect(header).toMatch(/^[\x20-\x7e]+$/)
  })
})
