// FICTIONAL sample data for the UI skeleton. Every screen that renders it shows <DemoNotice />.
// Replaced screen by screen with real API calls as backend phases land (docs/IMPLEMENTATION_PLAN.md).
// No real clinics, people or phone numbers.

import type {
  Appointment,
  AppointmentStatus,
  AuditEntry,
  ClinicSummary,
  DailySummary,
  Doctor,
  Money,
  PaymentRecord,
  ServiceOffer,
  StaffMember,
} from '../domain/types'

const TZ = 'Asia/Tashkent'
const TASHKENT_OFFSET_HOURS = 5 // Uzbekistan has no DST; real data carries the IANA zone instead.

const uzs = (amount: number): Money => ({ amount: String(amount), currency: 'UZS' })

/** UTC ISO for a Tashkent wall-clock time, `dayOffset` days from today (Tashkent calendar). */
export function tashkentAt(hour: number, minute: number, dayOffset = 0, now = new Date()): string {
  const local = new Date(now.getTime() + TASHKENT_OFFSET_HOURS * 3_600_000)
  return new Date(
    Date.UTC(local.getUTCFullYear(), local.getUTCMonth(), local.getUTCDate() + dayOffset, hour - TASHKENT_OFFSET_HOURS, minute),
  ).toISOString()
}

const minutesFromNow = (minutes: number) => new Date(Date.now() + minutes * 60_000).toISOString()

const services = {
  consult: { serviceId: 's-consult', name: 'Konsultatsiya va ko‘rik', category: 'Diagnostika', durationMinutes: 30 },
  cleaning: { serviceId: 's-clean', name: 'Professional tish tozalash', category: 'Gigiyena', durationMinutes: 60 },
  filling: { serviceId: 's-fill', name: 'Plomba qo‘yish', category: 'Davolash', durationMinutes: 60 },
  extraction: { serviceId: 's-extract', name: 'Tish olish', category: 'Jarrohlik', durationMinutes: 45 },
  whitening: { serviceId: 's-white', name: 'Tish oqartirish', category: 'Estetika', durationMinutes: 90 },
  implant: { serviceId: 's-implant', name: 'Implant o‘rnatish', category: 'Jarrohlik', durationMinutes: 120 },
} as const

function offer(base: (typeof services)[keyof typeof services], price: number, priceKind: ServiceOffer['priceKind'] = 'Fixed'): ServiceOffer {
  return { ...base, price: uzs(price), priceKind }
}

const weekdays = { open: '09:00', close: '19:00' }
const saturday = { open: '10:00', close: '16:00' }

export const demoClinics: ClinicSummary[] = [
  {
    id: 'c-chilonzor',
    name: 'Demo klinika «Chilonzor»',
    description: 'Oilaviy stomatologiya: bolalar va kattalar uchun davolash, gigiyena va estetika.',
    rating: 4.8,
    reviewCount: 214,
    verification: 'Approved',
    branches: [
      {
        id: 'b-chil-1',
        name: 'Chilonzor filiali',
        district: 'Chilonzor',
        address: 'Namuna ko‘chasi, 12-uy',
        timeZone: TZ,
        phone: '+998 71 000 00 01',
        hours: { 1: weekdays, 2: weekdays, 3: weekdays, 4: weekdays, 5: weekdays, 6: saturday },
        services: [offer(services.consult, 100_000), offer(services.cleaning, 350_000), offer(services.filling, 450_000, 'From'), offer(services.extraction, 300_000, 'From')],
      },
      {
        id: 'b-chil-2',
        name: 'Qatortol filiali',
        district: 'Chilonzor',
        address: 'Sinov mavzesi, 4-uy',
        timeZone: TZ,
        phone: '+998 71 000 00 02',
        hours: { 1: weekdays, 2: weekdays, 3: weekdays, 4: weekdays, 5: weekdays },
        services: [offer(services.consult, 90_000), offer(services.filling, 420_000, 'From'), offer(services.whitening, 1_200_000)],
      },
    ],
  },
  {
    id: 'c-yunusobod',
    name: 'Demo klinika «Yunusobod»',
    description: 'Implantologiya va murakkab jarrohlik, zamonaviy diagnostika.',
    rating: 4.6,
    reviewCount: 98,
    verification: 'Approved',
    branches: [
      {
        id: 'b-yun-1',
        name: 'Yunusobod markaziy',
        district: 'Yunusobod',
        address: 'Misol shoh ko‘chasi, 7-uy',
        timeZone: TZ,
        phone: '+998 71 000 00 03',
        hours: { 1: weekdays, 2: weekdays, 3: weekdays, 4: weekdays, 5: weekdays, 6: saturday, 7: saturday },
        services: [offer(services.consult, 150_000), offer(services.implant, 6_500_000, 'From'), offer(services.extraction, 400_000, 'From'), offer(services.cleaning, 400_000)],
      },
    ],
  },
  {
    id: 'c-mirzo',
    name: 'Demo klinika «Mirzo Ulug‘bek»',
    description: 'Estetik stomatologiya va tish oqartirish.',
    rating: 4.3,
    reviewCount: 41,
    verification: 'Approved',
    branches: [
      {
        id: 'b-mir-1',
        name: 'Buyuk Ipak yo‘li filiali',
        district: 'Mirzo Ulug‘bek',
        address: 'Tajriba ko‘chasi, 21-uy',
        timeZone: TZ,
        phone: '+998 71 000 00 04',
        hours: { 1: weekdays, 2: weekdays, 3: weekdays, 4: weekdays, 5: weekdays, 6: saturday },
        services: [offer(services.consult, 80_000), offer(services.whitening, 950_000), offer(services.cleaning, 300_000)],
      },
    ],
  },
]

/** Clinic awaiting platform verification (admin screen only; never listed publicly). */
export const demoPendingClinics: ClinicSummary[] = [
  { id: 'c-new-1', name: 'Demo klinika «Sergeli»', description: 'Yangi ro‘yxatdan o‘tgan klinika.', rating: 0, reviewCount: 0, verification: 'Pending', branches: [] },
  { id: 'c-new-2', name: 'Demo klinika «Olmazor»', description: 'Hujjatlar yuklangan.', rating: 0, reviewCount: 0, verification: 'Pending', branches: [] },
]

export const demoDoctors: Doctor[] = [
  { id: 'd-1', clinicId: 'c-chilonzor', name: 'Dr. Namuna A.', branchIds: ['b-chil-1'], serviceIds: ['s-consult', 's-fill', 's-extract'] },
  { id: 'd-2', clinicId: 'c-chilonzor', name: 'Dr. Misol B.', branchIds: ['b-chil-1', 'b-chil-2'], serviceIds: ['s-consult', 's-clean', 's-white'] },
  { id: 'd-3', clinicId: 'c-chilonzor', name: 'Dr. Sinov D.', branchIds: ['b-chil-2'], serviceIds: ['s-consult', 's-fill'] },
]

function appointment(
  id: string,
  status: AppointmentStatus,
  startsAt: string,
  durationMinutes: number,
  service: string,
  patientName: string,
  doctorName: string | null,
  price: number,
  extra: Partial<Appointment> = {},
): Appointment {
  return {
    id,
    clinicId: 'c-chilonzor',
    clinicName: 'Demo klinika «Chilonzor»',
    branchId: 'b-chil-1',
    branchName: 'Chilonzor filiali',
    serviceName: service,
    patientName,
    patientPhoneMasked: '+998 ** *** ** ' + id.slice(-2).padStart(2, '0'),
    doctorName,
    startsAt,
    endsAt: new Date(new Date(startsAt).getTime() + durationMinutes * 60_000).toISOString(),
    status,
    expiresAt: null,
    quotedPrice: uzs(price),
    timeZone: TZ,
    ...extra,
  }
}

/** Clinic-side view (Chilonzor branch). Pending requests expire relative to "now". */
export function demoClinicAppointments(): Appointment[] {
  return [
    appointment('a-11', 'Pending', tashkentAt(15, 0, 1), 60, 'Plomba qo‘yish', 'Aziza K.', null, 450_000, { expiresAt: minutesFromNow(24) }),
    appointment('a-12', 'Pending', tashkentAt(11, 30, 2), 30, 'Konsultatsiya va ko‘rik', 'Bekzod T.', null, 100_000, { expiresAt: minutesFromNow(7) }),
    appointment('a-13', 'Pending', tashkentAt(17, 0, 1), 60, 'Professional tish tozalash', 'Dilnoza R.', null, 350_000, { expiresAt: minutesFromNow(29) }),
    appointment('a-21', 'Confirmed', tashkentAt(9, 0), 30, 'Konsultatsiya va ko‘rik', 'Jasur M.', 'Dr. Namuna A.', 100_000),
    appointment('a-22', 'Confirmed', tashkentAt(10, 0), 60, 'Professional tish tozalash', 'Malika S.', 'Dr. Misol B.', 350_000),
    appointment('a-23', 'Confirmed', tashkentAt(12, 0), 45, 'Tish olish', 'Otabek N.', 'Dr. Namuna A.', 300_000),
    appointment('a-24', 'Confirmed', tashkentAt(14, 30), 60, 'Plomba qo‘yish', 'Nodira X.', 'Dr. Namuna A.', 450_000),
    appointment('a-25', 'Confirmed', tashkentAt(16, 0), 30, 'Konsultatsiya va ko‘rik', 'Sardor Y.', 'Dr. Misol B.', 100_000),
    appointment('a-31', 'Completed', tashkentAt(9, 30, -1), 60, 'Plomba qo‘yish', 'Kamola E.', 'Dr. Namuna A.', 450_000),
    appointment('a-32', 'NoShow', tashkentAt(11, 0, -1), 30, 'Konsultatsiya va ko‘rik', 'Rustam O.', 'Dr. Misol B.', 100_000),
  ]
}

/** Patient-side view: one patient's own appointments across clinics. */
export function demoMyAppointments(): Appointment[] {
  return [
    appointment('m-01', 'Pending', tashkentAt(15, 0, 1), 60, 'Plomba qo‘yish', 'Siz', null, 450_000, { expiresAt: minutesFromNow(24) }),
    appointment('m-02', 'Confirmed', tashkentAt(10, 0, 4), 90, 'Tish oqartirish', 'Siz', 'Dr. Misol B.', 1_200_000, {
      branchId: 'b-chil-2',
      branchName: 'Qatortol filiali',
    }),
    appointment('m-03', 'Completed', tashkentAt(16, 0, -20), 60, 'Professional tish tozalash', 'Siz', 'Dr. Misol B.', 350_000),
    appointment('m-04', 'CancelledByPatient', tashkentAt(9, 0, -35), 30, 'Konsultatsiya va ko‘rik', 'Siz', null, 150_000, {
      clinicId: 'c-yunusobod',
      clinicName: 'Demo klinika «Yunusobod»',
      branchId: 'b-yun-1',
      branchName: 'Yunusobod markaziy',
    }),
  ]
}

/** Display-only free start times for a day. The server computes real availability (Phase 4). */
export function demoSlots(dateIso: string, durationMinutes: number): string[] {
  const seed = [...dateIso].reduce((sum, ch) => sum + ch.charCodeAt(0), 0)
  const slots: string[] = []
  for (let minutes = 9 * 60; minutes + durationMinutes <= 19 * 60; minutes += 30) {
    if ((minutes / 30 + seed) % 4 === 0) continue // pretend some times are taken
    slots.push(`${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`)
  }
  return slots
}

export const demoStaff: StaffMember[] = [
  { id: 'u-1', name: 'Demo Egasi', role: 'ClinicOwner', scope: 'Clinic', branchNames: [], status: 'Active' },
  { id: 'u-2', name: 'Demo Administrator', role: 'ClinicAdmin', scope: 'Clinic', branchNames: [], status: 'Active' },
  { id: 'u-3', name: 'Demo Qabulxona', role: 'Receptionist', scope: 'Branches', branchNames: ['Chilonzor filiali'], status: 'Active' },
  { id: 'u-4', name: 'Dr. Namuna A.', role: 'Doctor', scope: 'Branches', branchNames: ['Chilonzor filiali'], status: 'Active' },
  { id: 'u-5', name: 'Demo Moliyachi', role: 'FinanceManager', scope: 'Clinic', branchNames: [], status: 'Invited' },
]

export const demoPayments: PaymentRecord[] = [
  { id: 'p-1', appointmentId: 'a-31', patientName: 'Kamola E.', method: 'Card', direction: 'In', amount: uzs(450_000), status: 'Recorded', recordedAt: tashkentAt(10, 40, -1) },
  { id: 'p-2', appointmentId: 'a-21', patientName: 'Jasur M.', method: 'Cash', direction: 'In', amount: uzs(100_000), status: 'Recorded', recordedAt: tashkentAt(9, 35) },
  { id: 'p-3', appointmentId: 'a-22', patientName: 'Malika S.', method: 'Card', direction: 'In', amount: uzs(350_000), status: 'Failed', recordedAt: tashkentAt(11, 5) },
  { id: 'p-4', appointmentId: 'a-31', patientName: 'Kamola E.', method: 'Card', direction: 'Refund', amount: uzs(50_000), status: 'Recorded', recordedAt: tashkentAt(12, 10) },
]

export const demoDailySummaries: DailySummary[] = [0, -1, -2, -3, -4].map((offset, index) => ({
  localDate: tashkentAt(12, 0, offset).slice(0, 10),
  branchName: 'Chilonzor filiali',
  completedVisits: [3, 7, 6, 9, 5][index] ?? 0,
  collected: uzs([100_000, 2_150_000, 1_800_000, 3_050_000, 1_400_000][index] ?? 0),
  refunded: uzs([50_000, 0, 0, 120_000, 0][index] ?? 0),
}))

export const demoAudit: AuditEntry[] = [
  { id: 'l-1', occurredAt: minutesFromNow(-12), actor: 'Demo Qabulxona', action: 'appointment.confirm', target: 'Appointment a-21' },
  { id: 'l-2', occurredAt: minutesFromNow(-45), actor: 'Demo Moliyachi', action: 'payment.refund', target: 'Payment p-4' },
  { id: 'l-3', occurredAt: minutesFromNow(-180), actor: 'Platform admin', action: 'clinic.approve', target: 'Clinic c-mirzo' },
  { id: 'l-4', occurredAt: minutesFromNow(-600), actor: 'Demo Egasi', action: 'staff.invite', target: 'Demo Moliyachi' },
]

export function findClinic(id: string | undefined): ClinicSummary | undefined {
  return demoClinics.find((c) => c.id === id)
}

/** Cheapest offer across branches — display aid only. */
export function lowestPrice(clinic: ClinicSummary): Money | undefined {
  const prices = clinic.branches.flatMap((b) => b.services.map((s) => s.price))
  return prices.reduce<Money | undefined>((min, p) => (min === undefined || BigInt(p.amount) < BigInt(min.amount) ? p : min), undefined)
}
