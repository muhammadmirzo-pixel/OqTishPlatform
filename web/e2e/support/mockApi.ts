// FICTIONAL API answers for the UI suite (a11y, layout, screenshots) — no real clinics, people or
// numbers. Journeys against the real backend live in e2e/journeys and use no mocks.
import type { Page, Route } from '@playwright/test'
import type { AppointmentDto, CalendarBlock, CalendarDto, Me } from '../../src/api/types'
import { appointment, clinicDetail, membership, patientMe } from '../../src/test/fixtures'

export type Persona = 'guest' | 'owner' | 'admin'

const TZ_OFFSET = '+05:00'
const money = (amount: string) => ({ amount, currency: 'UZS' as const })
const page1 = <T>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length })
const today = () => new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' })
const at = (date: string, hhmm: string) => new Date(`${date}T${hhmm}:00${TZ_OFFSET}`).toISOString()
const addDays = (date: string, n: number) => {
  const d = new Date(`${date}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + n)
  return d.toISOString().slice(0, 10)
}

const ownerPerms = [
  'appointment.view', 'appointment.triage', 'appointment.cancel', 'appointment.outcome', 'appointment.create_walkin',
  'payment.record', 'payment.refund', 'schedule.manage', 'catalog.manage', 'branch.manage', 'staff.manage',
  'finance.report.view', 'finance.settlement.view', 'audit.view', 'clinic.profile.manage',
]

function meFor(persona: Persona): Me | null {
  if (persona === 'guest') return null
  if (persona === 'owner') return { ...patientMe, id: 'u-owner', displayName: 'Test Egasi', patient: null, totpEnabled: true, memberships: [membership(ownerPerms, { role: 'ClinicOwner' })] }
  return {
    ...patientMe,
    id: 'u-admin',
    displayName: 'Test Admin',
    patient: null,
    totpEnabled: true,
    platformPermissions: ['platform.clinic.verify', 'platform.account.suspend', 'platform.reports.handle', 'platform.finance.settle', 'platform.tenant-data.read', 'platform.audit.view'],
  }
}

function todaysAppointments(): AppointmentDto[] {
  const d = today()
  const mk = (id: string, s: string, e: string, status: AppointmentDto['status'], name: string, doctor: string | null) =>
    appointment({ id, startsAt: at(d, s), endsAt: at(d, e), status, contactName: name, doctor: doctor ? { id: 'd1', name: doctor } : null, expiresAt: status === 'Pending' ? new Date(Date.now() + 20 * 60_000).toISOString() : null })
  return [mk('a1', '09:00', '09:30', 'Completed', 'Test Bemor 1', 'Dr Alfa'), mk('a2', '10:00', '11:00', 'Confirmed', 'Test Bemor 2', 'Dr Beta'), mk('a3', '14:00', '14:30', 'Pending', 'Test Bemor 3', null)]
}

function calendarFor(date: string): CalendarDto {
  const block = (kind: CalendarBlock['kind'], s: string, e: string, extra: Partial<CalendarBlock> = {}): CalendarBlock => ({
    kind, startsAt: at(date, s), endsAt: at(date, e), appointmentId: null, status: null, serviceName: null, contactName: null, otherBranch: false, blockId: null, ...extra,
  })
  return {
    date,
    timeZone: 'Asia/Tashkent',
    branchId: 'b1',
    doctors: [
      { id: 'd1', name: 'Dr Alfa', workingHours: [{ start: '09:00', end: '18:00' }], blocks: [block('Appointment', '09:00', '09:30', { appointmentId: 'a1', status: 'Completed', serviceName: 'Konsultatsiya', contactName: 'Test Bemor 1' }), block('TimeOff', '13:00', '14:00', { blockId: 't1' })] },
      { id: 'd2', name: 'Dr Beta', workingHours: [{ start: '10:00', end: '19:00' }], blocks: [block('Appointment', '10:00', '11:00', { appointmentId: 'a2', status: 'Confirmed', serviceName: 'Plomba', contactName: 'Test Bemor 2' }), block('Hold', '14:00', '14:30', { appointmentId: 'a3', status: 'Pending', serviceName: 'Konsultatsiya', contactName: 'Test Bemor 3' })] },
    ],
  }
}

function daily(from: string, to: string) {
  const rows = []
  for (let d = from, i = 0; d <= to; d = addDays(d, 1), i++) {
    const charged = ((i * 7) % 5) * 400_000 + 600_000
    rows.push({ date: d, branchId: null, visits: 4, charged: money(`${charged}.00`), collected: money(`${charged - 200_000}.00`), refunded: money('0.00'), commission: money('0.00'), priorPeriodRefunded: money('0.00') })
  }
  return rows
}

type Reply = { status?: number; body?: unknown }

function answer(persona: Persona, url: URL, method: string): Reply | undefined {
  const p = url.pathname.replace(/^\/api/, '')
  const q = url.searchParams
  if (p === '/me') {
    const me = meFor(persona)
    return me ? { body: me } : { status: 401, body: { title: 'auth.unauthenticated', status: 401, code: 'auth.unauthenticated' } }
  }
  if (method !== 'GET') return undefined
  if (p === '/telegram/config') return { body: { enabled: false, botUsername: null, miniAppUrl: null, botId: null } }
  if (p === '/public/facets') return { body: { districts: ['Yunusobod', 'Chilonzor'], categories: [{ key: 'therapy', name: 'Terapiya' }, { key: 'hygiene', name: 'Gigiyena' }], services: [{ id: 's1', name: 'Konsultatsiya', category: 'therapy' }] } }
  if (p === '/public/clinics') {
    const card = (id: string, name: string, rating: number | null) => ({ id, name, rating, reviewCount: rating ? 12 : 0, minPrice: money('100000.00'), branchCount: 1, districts: ['Yunusobod'], distanceKm: null, nextFreeSlot: null })
    return { body: page1([card('c1', 'Namuna Klinika', 4.5), card('c2', 'Test Stomatologiya', 4.1), card('c3', 'Yangi Klinika', null)]) }
  }
  if (p === '/public/clinics/c1') return { body: clinicDetail }
  if (p === '/public/clinics/c1/reviews') return { body: page1([{ id: 'r1', rating: 5, text: 'Test sharh matni.', authorName: 'Test B.', createdAt: '2026-10-01T05:00:00Z', branchName: 'Markaziy filial' }]) }

  if (p === '/clinics/c1/branches') return { body: clinicDetail.branches.map((b) => ({ id: b.id, name: b.name, district: b.district, address: b.address, latitude: null, longitude: null, phone: b.phone, timeZone: b.timeZone, isActive: true, hours: b.hours })) }
  if (p === '/clinics/c1/dashboard') return { body: { date: today(), branchId: null, pending: 1, confirmed: 1, completed: 1, noShow: 0, collected: money('450000.00') } }
  if (p === '/clinics/c1/appointments') return { body: page1(q.get('status') === 'Pending' ? todaysAppointments().filter((a) => a.status === 'Pending') : todaysAppointments()) }
  if (/^\/clinics\/c1\/appointments\/a\d+\/eligible-doctors$/.test(p)) return { body: [{ id: 'd1', name: 'Dr Alfa', withinWorkingHours: true, provisional: false }] }
  if (p === '/clinics/c1/calendar') return { body: calendarFor(q.get('date') ?? today()) }
  if (p === '/clinics/c1/reports/daily') return { body: daily(q.get('from') ?? today(), q.get('to') ?? today()) }
  if (p === '/clinics/c1/doctors') return { body: [{ id: 'd1', displayName: 'Dr Alfa', isActive: true, branchIds: ['b1'], serviceIds: ['s1'], userId: null }] }

  if (p === '/admin/dashboard') return { body: { clinicsByStatus: { Pending: 1, Approved: 3, Rejected: 0, Suspended: 1 }, appointmentsToday: 7, openReports: 1, activeUsers: 25, suspendedUsers: 1 } }
  if (p === '/admin/clinics') {
    const c = (id: string, name: string, verificationStatus: string, statusReason: string | null = null) => ({ id, name, legalName: `${name} MChJ`, phone: '+998711234567', verificationStatus, isSuspended: verificationStatus === 'Suspended', statusReason, branchCount: 1, createdAt: '2026-09-01T05:00:00Z' })
    return { body: page1([c('c1', 'Namuna Klinika', 'Approved'), c('c2', 'Yangi Klinika', 'Pending'), c('c3', 'Eski Klinika', 'Suspended', 'Test sababi')]) }
  }
  return undefined
}

/** Serves the persona's canned API; anything else answers 404 so a missing mock shows as an error state. */
export async function mockApi(page: Page, persona: Persona) {
  await page.route((url) => url.pathname.startsWith('/health/'), (route) => route.fulfill({ status: 200, body: 'Healthy' }))
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    (route: Route) => {
      const request = route.request()
      const reply = answer(persona, new URL(request.url()), request.method()) ?? { status: 404, body: { title: 'common.not_found', status: 404, code: 'common.not_found' } }
      return route.fulfill({ status: reply.status ?? 200, contentType: (reply.status ?? 200) >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(reply.body) })
    },
  )
}
