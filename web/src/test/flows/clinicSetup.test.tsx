import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { serviceCategories, type BranchDto, type DoctorDto, type InvitationDto, type ServiceDto, type StaffMemberDto } from '../../api/types'
import { staffMe } from '../fixtures'
import { meHandler, mockFetch, problem, renderApp, type Handler } from '../harness'

// FICTIONAL data shaped like docs/API.md §4.1.
const branch = (id: string, name: string): BranchDto => ({
  id,
  name,
  district: 'Yunusobod',
  address: 'Test 1',
  latitude: null,
  longitude: null,
  phone: '',
  timeZone: 'Asia/Tashkent',
  isActive: true,
  hours: [],
})
const b1 = branch('b1', 'Markaziy filial')
const b2 = branch('b2', 'Chilonzor filial')
const service: ServiceDto = {
  id: 's1',
  name: 'Konsultatsiya',
  category: 'diagnostics',
  isActive: true,
  offers: [{ branchId: 'b1', price: { amount: '100000.00', currency: 'UZS' }, priceKind: 'Fixed', durationMinutes: 30, isActive: true }],
}
const doctor: DoctorDto = { id: 'd1', displayName: 'Dr Alfa', isActive: true, branchIds: ['b1'], serviceIds: ['s1'], userId: 'u-doc' }

describe('services (catalog.manage)', () => {
  it('lets a clinic-wide manager create a service with a fixed, localised category key', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(staffMe(['catalog.manage'], { role: 'ClinicAdmin' })),
      ['GET', '/clinics/c1/branches', { body: [b1] }],
      ['GET', '/clinics/c1/services', { body: [service] }],
      ['POST', '/clinics/c1/services', { status: 201, body: { ...service, id: 's2', name: 'Oqartirish', category: 'whitening', offers: [] } }],
    ])
    renderApp('/clinic/c1/services', fetchMock)
    // Category label comes from the UI catalogue, not from the key.
    expect(await screen.findByText(/Diagnostics and consultation/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Add service' }))
    const dialog = await screen.findByRole('dialog')
    const select = within(dialog).getByLabelText('Category')
    const options = within(select).getAllByRole('option').map((o) => o.getAttribute('value'))
    expect(options).toEqual(['', ...serviceCategories])
    await user.type(within(dialog).getByLabelText('Name'), 'Oqartirish')
    await user.selectOptions(select, 'whitening')
    expect(within(select).getByRole('option', { name: 'Whitening and aesthetics' })).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(fetchMock.find('POST', '/clinics/c1/services')).toHaveLength(1))
    expect(fetchMock.find('POST', '/clinics/c1/services')[0]!.body).toEqual({ name: 'Oqartirish', category: 'whitening', isActive: true })
  })

  it('lets a Branches-scoped manager set prices but not create or edit services', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(staffMe(['catalog.manage'], { role: 'ClinicAdmin', scope: 'Branches', branchIds: ['b1'] })),
      ['GET', '/clinics/c1/branches', { body: [b1] }],
      ['GET', '/clinics/c1/services', { body: [service] }],
      ['PUT', '/clinics/c1/branches/b1/services/s1', { status: 204 }],
    ])
    renderApp('/clinic/c1/services', fetchMock)
    const card = (await screen.findByText('Konsultatsiya')).closest('li')!
    expect(screen.queryByRole('button', { name: 'Add service' })).toBeNull()
    expect(screen.getByText(/created and edited by staff with clinic-wide access/)).toBeInTheDocument()
    // The only "Edit" button is the branch offer's one (row), not the service's.
    const row = within(card).getByRole('row', { name: /Markaziy filial/ })
    await user.click(within(row).getByRole('button', { name: 'Edit' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    // PUT …/services/{id} answers 204: the dialog closes and the list reloads.
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(fetchMock.find('PUT', '/clinics/c1/branches/b1/services/s1')[0]!.body).toEqual({
      price: { amount: '100000.00', currency: 'UZS' },
      priceKind: 'Fixed',
      durationMinutes: 30,
      isActive: true,
    })
    expect(await screen.findByText('Saved')).toBeInTheDocument()
  })
})

describe('branches (branch.manage)', () => {
  it('offers only the allowed time zones, keeps phone optional and pairs coordinates', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([
      meHandler(staffMe(['branch.manage'], { role: 'ClinicOwner' })),
      ['GET', '/clinics/c1/branches', { body: [b1] }],
      ['POST', '/clinics/c1/branches', { status: 201, body: b2 }],
    ])
    renderApp('/clinic/c1/branches', fetchMock)
    await user.click(await screen.findByRole('button', { name: 'Add branch' }))
    const dialog = await screen.findByRole('dialog')
    const tz = within(dialog).getByLabelText('Time zone')
    expect(within(tz).getAllByRole('option').map((o) => o.getAttribute('value'))).toEqual(['Asia/Tashkent', 'Asia/Samarkand'])
    await user.type(within(dialog).getByLabelText('Name'), 'Chilonzor filial')
    await user.type(within(dialog).getByLabelText('District'), 'Chilonzor')
    await user.type(within(dialog).getByLabelText('Address'), 'Test 2')
    await user.type(within(dialog).getByLabelText(/Latitude/), '41.2856')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(within(dialog).getAllByText(/go together/).length).toBeGreaterThan(0)
    expect(fetchMock.find('POST', '/clinics/c1/branches')).toHaveLength(0)

    await user.type(within(dialog).getByLabelText(/Longitude/), '69.2034')
    await user.selectOptions(tz, 'Asia/Samarkand')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(fetchMock.find('POST', '/clinics/c1/branches')).toHaveLength(1))
    expect(fetchMock.find('POST', '/clinics/c1/branches')[0]!.body).toEqual({
      name: 'Chilonzor filial',
      district: 'Chilonzor',
      address: 'Test 2',
      phone: '',
      timeZone: 'Asia/Samarkand',
      latitude: 41.2856,
      longitude: 69.2034,
      isActive: true,
    })
  })

  it('hides "Add branch" for a Branches-scoped manager (creating needs clinic-wide branch.manage)', async () => {
    renderApp(
      '/clinic/c1/branches',
      mockFetch([meHandler(staffMe(['branch.manage'], { role: 'ClinicAdmin', scope: 'Branches', branchIds: ['b1'] })), ['GET', '/clinics/c1/branches', { body: [b1] }]]),
    )
    // Editing an own branch is allowed.
    expect(await screen.findByRole('button', { name: 'Edit' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add branch' })).toBeNull()
  })
})

describe('doctors (schedule.manage, userId needs staff.manage)', () => {
  const routes = (perms: string[]): Handler[] => [
    meHandler(staffMe(perms, { role: 'ClinicAdmin' })),
    ['GET', '/clinics/c1/branches', { body: [b1] }],
    ['GET', '/clinics/c1/doctors', { body: [doctor] }],
    ['GET', '/clinics/c1/services', { body: [service] }],
    ['GET', '/clinics/c1/doctors/d1/working-hours', { body: [] }],
    ['GET', '/clinics/c1/doctors/d1/time-off', { body: [] }],
    ['PUT', '/clinics/c1/doctors/d1', { body: doctor }],
  ]

  it('sends the current userId back unchanged without staff.manage and hides the account picker', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([...routes(['schedule.manage'])])
    renderApp('/clinic/c1/doctors/d1', fetchMock)
    await user.click(await screen.findByRole('button', { name: 'Edit' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).queryByLabelText(/Staff account/)).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(fetchMock.find('PUT', '/clinics/c1/doctors/d1')).toHaveLength(1))
    expect(fetchMock.find('PUT', '/clinics/c1/doctors/d1')[0]!.body).toEqual({ displayName: 'Dr Alfa', isActive: true, branchIds: ['b1'], serviceIds: ['s1'], userId: 'u-doc' })
    expect(fetchMock.find('GET', '/clinics/c1/staff')).toHaveLength(0)
  })

  it('lets a manager with staff.manage link any clinic member and localises doctor_user_taken', async () => {
    const user = userEvent.setup()
    const member = (userId: string, displayName: string, role: StaffMemberDto['role']): StaffMemberDto => ({
      membershipId: `m-${userId}`,
      userId,
      displayName,
      phoneMasked: null,
      role,
      scope: 'Clinic',
      branchIds: [],
      status: 'Active',
    })
    const fetchMock = mockFetch([
      ...routes(['schedule.manage', 'staff.manage']).filter((r) => r[0] !== 'PUT'),
      ['GET', '/clinics/c1/staff', { body: [member('u-rec', 'Qabulxona Xodimi', 'Receptionist'), member('u-doc', 'Alfa Doktor', 'Doctor')] }],
      ['PUT', '/clinics/c1/doctors/d1', () => problem(409, 'schedule.doctor_user_taken')],
    ])
    renderApp('/clinic/c1/doctors/d1', fetchMock)
    await user.click(await screen.findByRole('button', { name: 'Edit' }))
    const dialog = await screen.findByRole('dialog')
    const picker = await within(dialog).findByLabelText(/Staff account/)
    expect(picker).toHaveValue('u-doc')
    // Doctor-role members first, but any member can be linked.
    expect(within(picker).getAllByRole('option').map((o) => o.getAttribute('value'))).toEqual(['', 'u-doc', 'u-rec'])
    await user.selectOptions(picker, 'u-rec')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('This user is already linked to another doctor.')).toBeInTheDocument()
    expect(fetchMock.find('PUT', '/clinics/c1/doctors/d1')[0]!.body).toMatchObject({ userId: 'u-rec' })
  })

  it('saves working hours (204) and shows schedule.hours_overlap from the server', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch([...routes(['schedule.manage']), ['PUT', '/clinics/c1/doctors/d1/working-hours', () => problem(422, 'schedule.hours_overlap')]])
    renderApp('/clinic/c1/doctors/d1', fetchMock)
    const section = (await screen.findByRole('heading', { name: 'Weekly schedule' })).closest('section')!
    await user.click(await within(section).findByRole('button', { name: 'Save' }))
    expect(await within(section).findByText('Working-hour intervals overlap on a weekday (also across branches).')).toBeInTheDocument()
    expect(fetchMock.find('PUT', '/clinics/c1/doctors/d1/working-hours')[0]!.body).toEqual([])
  })
})

describe('staff (staff.manage)', () => {
  it('marks expired invitations and keeps a Branches-scoped manager to Branches scope', async () => {
    const user = userEvent.setup()
    const inv = (id: string, expiresAt: string): InvitationDto => ({ id, phoneMasked: `+998*****00${id}`, role: 'Receptionist', scope: 'Branches', branchIds: ['b1'], createdAt: '2026-01-01T00:00:00Z', expiresAt })
    const fetchMock = mockFetch([
      meHandler(staffMe(['staff.manage'], { role: 'ClinicAdmin', scope: 'Branches', branchIds: ['b1'] })),
      ['GET', '/clinics/c1/branches', { body: [b1] }],
      ['GET', '/clinics/c1/staff', { body: [] }],
      ['GET', '/clinics/c1/invitations', { body: [inv('1', '2000-01-01T00:00:00Z'), inv('2', '2999-01-01T00:00:00Z')] }],
      ['POST', '/clinics/c1/invitations', () => problem(409, 'staff.already_member')],
    ])
    renderApp('/clinic/c1/staff', fetchMock)
    const expired = (await screen.findByText('+998*****001')).closest('li')!
    expect(within(expired).getByText('Expired')).toBeInTheDocument()
    expect(within(screen.getByText('+998*****002').closest('li')!).queryByText('Expired')).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Invite staff' }))
    const dialog = await screen.findByRole('dialog')
    const scope = within(dialog).getByLabelText('Access scope')
    expect(within(scope).getAllByRole('option').map((o) => o.getAttribute('value'))).toEqual(['Branches'])
    await user.type(within(dialog).getByLabelText('Phone'), '+998 90 123 45 67')
    await user.selectOptions(within(dialog).getByLabelText('Role'), 'Receptionist')
    await user.click(within(dialog).getByLabelText('Markaziy filial'))
    await user.click(within(dialog).getByRole('button', { name: 'Send invitation' }))
    expect(await within(dialog).findByText('The owner of this phone number is already a member of the clinic.')).toBeInTheDocument()
  })
})

describe('clinic profile', () => {
  it('shows the platform reason of a rejected clinic and is read-only for a Branches-scoped member', async () => {
    renderApp(
      '/clinic/c1/profile',
      mockFetch([
        meHandler(staffMe(['branch.manage'], { role: 'ClinicAdmin', scope: 'Branches', branchIds: ['b1'], clinicStatus: 'Rejected' })),
        ['GET', '/clinics/c1/branches', { body: [b1] }],
        [
          'GET',
          '/clinics/c1',
          {
            body: { id: 'c1', name: 'Namuna Klinika', legalName: '', description: '', phone: '', verificationStatus: 'Rejected', statusReason: 'Litsenziya nusxasi yo‘q', createdAt: '2026-10-18T10:00:00Z' },
          },
        ],
      ]),
    )
    expect(await screen.findByText('Platform note: Litsenziya nusxasi yo‘q')).toBeInTheDocument()
    expect(screen.getByText('Only a clinic-wide owner or administrator can change the profile.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull()
  })
})
