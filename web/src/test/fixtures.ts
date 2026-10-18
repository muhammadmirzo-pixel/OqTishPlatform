// FICTIONAL test fixtures (no real clinics, people or numbers). Used only by tests.
import type { AppointmentDto, ClinicDetail, Me, Membership } from '../api/types'

export const patientMe: Me = {
  id: 'u-patient',
  displayName: 'Test Bemor',
  phoneVerified: true,
  phoneMasked: '+998*****4567',
  email: null,
  telegramLinked: false,
  totpEnabled: false,
  totpSetupRequired: false,
  patient: { fullName: 'Test Bemor', contactPhone: '+998901234567' },
  memberships: [],
  platformPermissions: [],
}

export function membership(permissions: string[], overrides: Partial<Membership> = {}): Membership {
  return {
    membershipId: 'm1',
    clinicId: 'c1',
    clinicName: 'Namuna Klinika',
    clinicStatus: 'Approved',
    role: 'Receptionist',
    scope: 'Clinic',
    branchIds: [],
    permissions,
    totpSetupRequired: false,
    ...overrides,
  }
}

export function staffMe(permissions: string[], overrides: Partial<Membership> = {}): Me {
  return { ...patientMe, id: 'u-staff', displayName: 'Test Xodim', patient: null, memberships: [membership(permissions, overrides)] }
}

export const clinicDetail: ClinicDetail = {
  id: 'c1',
  name: 'Namuna Klinika',
  description: 'Fictional clinic for tests',
  rating: 4.5,
  reviewCount: 3,
  branches: [
    {
      id: 'b1',
      name: 'Markaziy filial',
      district: 'Yunusobod',
      address: 'Test ko‘chasi 1',
      latitude: null,
      longitude: null,
      timeZone: 'Asia/Tashkent',
      phone: '+998711234567',
      hours: [1, 2, 3, 4, 5, 6].map((weekday) => ({ weekday: weekday as 1, open: '09:00', close: '18:00' })),
      services: [
        { serviceId: 's1', name: 'Konsultatsiya', category: 'therapy', price: { amount: '100000.00', currency: 'UZS' }, priceKind: 'Fixed', durationMinutes: 30 },
        { serviceId: 's2', name: 'Plomba', category: 'therapy', price: { amount: '250000.00', currency: 'UZS' }, priceKind: 'From', durationMinutes: 60 },
      ],
    },
  ],
}

export function appointment(overrides: Partial<AppointmentDto> = {}): AppointmentDto {
  return {
    id: 'a1',
    clinicId: 'c1',
    clinicName: 'Namuna Klinika',
    branchId: 'b1',
    branchName: 'Markaziy filial',
    timeZone: 'Asia/Tashkent',
    serviceId: 's1',
    serviceName: 'Konsultatsiya',
    startsAt: '2026-10-20T05:00:00Z',
    endsAt: '2026-10-20T05:30:00Z',
    status: 'Pending',
    expiresAt: '2026-10-19T04:30:00Z',
    quotedPrice: { amount: '100000.00', currency: 'UZS' },
    chargedAmount: null,
    doctor: null,
    contactName: 'Test Bemor',
    contactPhone: '+998901234567',
    source: 'Patient',
    canCancel: true,
    createdAt: '2026-10-19T04:00:00Z',
    history: [{ from: null, to: 'Pending', at: '2026-10-19T04:00:00Z', actor: 'Patient', reasonCode: null }],
    ...overrides,
  }
}
