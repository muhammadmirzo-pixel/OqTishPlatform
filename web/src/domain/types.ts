// Client-side shapes that mirror the planned API contracts (docs/ARCHITECTURE.md, docs/DATABASE.md).
// They describe what the server returns; the server stays authoritative for every value.

export type AppointmentStatus =
  | 'Pending'
  | 'Confirmed'
  | 'Rejected'
  | 'CancelledByPatient'
  | 'CancelledByClinic'
  | 'Expired'
  | 'Completed'
  | 'NoShow'

export type StaffRole = 'ClinicOwner' | 'ClinicAdmin' | 'Receptionist' | 'Doctor' | 'FinanceManager'

export type ClinicVerification = 'Pending' | 'Approved' | 'Suspended' | 'Rejected'

/** Money as the API sends it: decimal string + currency. Never parsed into floats for arithmetic. */
export interface Money {
  amount: string
  currency: 'UZS'
}

export interface ServiceOffer {
  serviceId: string
  name: string
  category: string
  price: Money
  priceKind: 'Fixed' | 'From'
  durationMinutes: number
}

export interface Branch {
  id: string
  name: string
  district: string
  address: string
  timeZone: string
  phone: string
  /** Opening hours per weekday (1 = Monday … 7 = Sunday), branch-local "HH:mm". */
  hours: Partial<Record<1 | 2 | 3 | 4 | 5 | 6 | 7, { open: string; close: string }>>
  services: ServiceOffer[]
}

export interface ClinicSummary {
  id: string
  name: string
  description: string
  rating: number
  reviewCount: number
  verification: ClinicVerification
  branches: Branch[]
}

export interface Doctor {
  id: string
  clinicId: string
  name: string
  branchIds: string[]
  serviceIds: string[]
}

export interface Appointment {
  id: string
  clinicId: string
  clinicName: string
  branchId: string
  branchName: string
  serviceName: string
  patientName: string
  patientPhoneMasked: string
  doctorName: string | null
  startsAt: string
  endsAt: string
  status: AppointmentStatus
  /** Only for Pending: server deadline (UTC ISO). Display only. */
  expiresAt: string | null
  quotedPrice: Money
  timeZone: string
}

export interface StaffMember {
  id: string
  name: string
  role: StaffRole
  scope: 'Clinic' | 'Branches'
  branchNames: string[]
  status: 'Active' | 'Invited' | 'Suspended'
}

export interface PaymentRecord {
  id: string
  appointmentId: string
  patientName: string
  method: 'Cash' | 'Card' | 'Transfer'
  direction: 'In' | 'Refund'
  amount: Money
  status: 'Recorded' | 'Failed'
  recordedAt: string
}

export interface DailySummary {
  localDate: string
  branchName: string
  completedVisits: number
  collected: Money
  refunded: Money
}

export interface AuditEntry {
  id: string
  occurredAt: string
  actor: string
  action: string
  target: string
}
