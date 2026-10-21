import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { patientMe } from '../fixtures'
import { meHandler, mockFetch, problem, renderApp } from '../harness'

describe('login', () => {
  it('signs in with phone OTP, honours the resend countdown and returns to ?next', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const fetchMock = mockFetch([
      meHandler(null),
      ['GET', '/telegram/config', { body: { enabled: false, botUsername: null, miniAppUrl: null } }],
      ['POST', '/auth/otp/request', { status: 202, body: { challengeId: 'ch-1', expiresInSeconds: 300, resendAfterSeconds: 30 } }],
      ['POST', '/auth/otp/verify', { body: patientMe }],
      ['GET', '/me/appointments', { body: { items: [], page: 1, pageSize: 20, totalCount: 0 } }],
    ])
    const { router } = renderApp('/login?next=%2Fme%2Fappointments', fetchMock)

    await user.click(await screen.findByRole('button', { name: 'Send code' }))
    expect(await screen.findByText('Please fill in this field')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Phone number'), '12345')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    expect(await screen.findByText(/^Enter the number as \+998 XX XXX XX XX/)).toBeInTheDocument()
    expect(fetchMock.find('POST', '/auth/otp/request')).toHaveLength(0)

    await user.clear(screen.getByLabelText('Phone number'))
    await user.type(screen.getByLabelText('Phone number'), '90 123 45 67')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    expect(fetchMock.find('POST', '/auth/otp/request')[0]?.body).toEqual({ phone: '+998901234567' })
    expect(fetchMock.find('POST', '/auth/otp/request')[0]?.headers['X-OqTish-Client']).toBe('web')

    const resend = await screen.findByRole('button', { name: /Resend in 30 s/ })
    expect(resend).toBeDisabled()
    await act(async () => {
      vi.advanceTimersByTime(31000)
    })
    expect(screen.getByRole('button', { name: 'Resend code' })).toBeEnabled()

    await user.type(screen.getByLabelText('SMS code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Verify' }))
    expect(fetchMock.find('POST', '/auth/otp/verify')[0]?.body).toEqual({ challengeId: 'ch-1', code: '123456' })
    await waitFor(() => expect(router.state.location.pathname).toBe('/me/appointments'))
  })

  it('shows a localised error for a wrong code', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(null),
      ['GET', '/telegram/config', { body: { enabled: false, botUsername: null, miniAppUrl: null } }],
      ['POST', '/auth/otp/request', { status: 202, body: { challengeId: 'ch-1', expiresInSeconds: 300, resendAfterSeconds: 0 } }],
      ['POST', '/auth/otp/verify', () => problem(400, 'auth.otp_invalid')],
    ])
    renderApp('/login', fetchMock)
    await user.type(await screen.findByLabelText('Phone number'), '+998901234567')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await user.type(await screen.findByLabelText('SMS code'), '000000')
    await user.click(screen.getByRole('button', { name: 'Verify' }))
    expect(await screen.findByText('The SMS code is wrong.')).toBeInTheDocument()
  })

  it('asks for the TOTP code when the server answers auth.totp_required', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(null),
      ['GET', '/telegram/config', { body: { enabled: false, botUsername: null, miniAppUrl: null } }],
      ['POST', '/auth/password/login', (call) => ((call.body as { totpCode?: string }).totpCode ? { body: { ...patientMe, patient: null } } : () => problem(401, 'auth.totp_required'))],
    ])
    const { router } = renderApp('/login?mode=staff', fetchMock)
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.type(screen.getByLabelText('Password'), 'correct horse battery')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    const totp = await screen.findByLabelText('Verification code (TOTP)')
    await user.type(totp, '654321')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(fetchMock.find('POST', '/auth/password/login')[1]?.body).toEqual({ email: 'owner@example.test', password: 'correct horse battery', totpCode: '654321' })
    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
  })

  it('asks for TOTP after the SMS code when the account has TOTP enabled', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(null),
      ['GET', '/telegram/config', { body: { enabled: false, botUsername: null, miniAppUrl: null } }],
      ['POST', '/auth/otp/request', { status: 202, body: { challengeId: 'ch-1', expiresInSeconds: 300, resendAfterSeconds: 60 } }],
      ['POST', '/auth/otp/verify', (call) => ((call.body as { totpCode?: string }).totpCode === '111222' ? { body: patientMe } : () => problem(401, 'auth.totp_required'))],
    ])
    const { router } = renderApp('/login', fetchMock)
    await user.type(await screen.findByLabelText('Phone number'), '00998901234567')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    expect(fetchMock.find('POST', '/auth/otp/request')[0]?.body).toEqual({ phone: '+998901234567' })
    await user.type(await screen.findByLabelText('SMS code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Verify' }))
    await user.type(await screen.findByLabelText('Verification code (TOTP)'), '111222')
    await user.click(screen.getByRole('button', { name: 'Verify' }))
    expect(fetchMock.find('POST', '/auth/otp/verify')[1]?.body).toEqual({ challengeId: 'ch-1', code: '123456', totpCode: '111222' })
    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
  })

  it('loads the Telegram Login Widget script only when Telegram is enabled', async () => {
    const fetchMock = mockFetch([meHandler(null), ['GET', '/telegram/config', { body: { enabled: false, botUsername: null, miniAppUrl: null } }]])
    renderApp('/login', fetchMock)
    await screen.findByRole('button', { name: 'Send code' })
    await waitFor(() => expect(fetchMock.find('GET', '/telegram/config')).toHaveLength(1))
    expect(document.querySelector('script[src*="telegram.org"]')).toBeNull()
  })
})

describe('route guards', () => {
  it('redirects anonymous visitors to /login?next=…', async () => {
    const { router } = renderApp('/me/appointments?status=Pending', mockFetch([meHandler(null), ['GET', '/telegram/config', { body: { enabled: false } }]]))
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(router.state.location.search).toBe(`?next=${encodeURIComponent('/me/appointments?status=Pending')}`)
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('shows the 403 state for a clinic the user is not a member of', async () => {
    renderApp('/clinic/other-clinic', mockFetch([meHandler(patientMe)]))
    expect(await screen.findByText('Access denied')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'My clinics' })).toHaveAttribute('href', '/clinic')
  })

  it('shows the 403 state on admin routes without platform permissions', async () => {
    renderApp('/admin', mockFetch([meHandler(patientMe)]))
    expect(await screen.findByText('Access denied')).toBeInTheDocument()
  })

  it('shows the TOTP state (not 403) for an admin whose grants wait for TOTP', async () => {
    renderApp('/admin', mockFetch([meHandler({ ...patientMe, totpSetupRequired: true, platformPermissions: [] })]))
    expect(await screen.findByText('Two-factor authentication required')).toBeInTheDocument()
  })

  it('links to TOTP setup when the API answers auth.totp_setup_required', async () => {
    const owner = { ...patientMe, memberships: [{ membershipId: 'm1', clinicId: 'c1', clinicName: 'Namuna', clinicStatus: 'Approved' as const, role: 'ClinicOwner' as const, scope: 'Clinic' as const, branchIds: [], permissions: ['branch.manage'], totpSetupRequired: false }] }
    renderApp('/clinic/c1', mockFetch([meHandler(owner), ['GET', '/clinics/c1/branches', () => problem(403, 'auth.totp_setup_required')]]))
    expect(await screen.findByText('Two-factor authentication required')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'Set up TOTP' })[0]).toHaveAttribute('href', '/me/security')
  })
})
