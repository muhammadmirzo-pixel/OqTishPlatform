// Types mirroring the DTOs in docs/API.md. Shapes marked "ASSUMED" are used by screens but are not
// spelled out in the contract yet; they are listed in docs/FRONTEND.md ("Contract gaps") so the
// backend can confirm or correct them. The server stays authoritative for every value.

/** Money as the API sends it: decimal string + currency. Never parsed into floats. */
export interface Money {
  amount: string
  currency: 'UZS'
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export type AppointmentStatus =
  | 'Pending'
  | 'Confirmed'
  | 'Rejected'
  | 'CancelledByPatient'
  | 'CancelledByClinic'
  | 'Expired'
  | 'Completed'
  | 'NoShow'

export const appointmentStatuses: readonly AppointmentStatus[] = [
  'Pending',
  'Confirmed',
  'Completed',
  'NoShow',
  'Rejected',
  'CancelledByPatient',
  'CancelledByClinic',
  'Expired',
]

export type StaffRole = 'ClinicOwner' | 'ClinicAdmin' | 'Receptionist' | 'Doctor' | 'FinanceManager'
export const staffRoles: readonly StaffRole[] = ['ClinicOwner', 'ClinicAdmin', 'Receptionist', 'Doctor', 'FinanceManager']

export type ClinicVerification = 'Pending' | 'Approved' | 'Suspended' | 'Rejected'
export const clinicVerifications: readonly ClinicVerification[] = ['Pending', 'Approved', 'Suspended', 'Rejected']

export type Weekday = 1 | 2 | 3 | 4 | 5 | 6 | 7
export const weekdays: readonly Weekday[] = [1, 2, 3, 4, 5, 6, 7]

export type MembershipScope = 'Clinic' | 'Branches'

// ---------------------------------------------------------------- session

export interface Membership {
  membershipId: string
  clinicId: string
  clinicName: string
  clinicStatus: ClinicVerification
  role: StaffRole
  scope: MembershipScope
  branchIds: string[]
  /** Effective permissions; [] while TOTP setup is required for this membership. */
  permissions: string[]
  totpSetupRequired: boolean
}

export interface Me {
  id: string
  displayName: string
  phoneVerified: boolean
  phoneMasked: string | null
  email: string | null
  telegramLinked: boolean
  totpEnabled: boolean
  /** Some membership or platform grant waits for TOTP to be enabled. */
  totpSetupRequired: boolean
  patient: { fullName: string; contactPhone: string | null } | null
  memberships: Membership[]
  /** Effective platform permissions; [] while TOTP setup is required. */
  platformPermissions: string[]
}

export interface OtpChallenge {
  challengeId: string
  expiresInSeconds: number
  resendAfterSeconds: number
}

/** Telegram Login Widget payload (https://core.telegram.org/widgets/login). */
export interface TelegramWidgetUser {
  id: number
  first_name: string
  last_name?: string
  username?: string
  photo_url?: string
  auth_date: number
  hash: string
}

export interface TelegramConfig {
  enabled: boolean
  botUsername: string | null
  miniAppUrl: string | null
  /**
   * ASSUMED extension: the bot's numeric id (public; the part of the token before ":"). With it the
   * website uses Telegram.Login.auth() and needs no eval; without it the widget's redirect mode is used.
   */
  botId?: number | null
}

export interface TotpSetup {
  secret: string
  otpauthUri: string
}

export interface TelegramLink {
  deepLink: string
  expiresAt: string
}

export interface SessionDto {
  id: string
  createdAt: string
  lastSeenAt: string
  expiresAt: string
  authMethod: 'Otp' | 'Password' | 'TelegramWebApp' | 'TelegramWidget'
  userAgent: string | null
  current: boolean
}

/** ASSUMED: GET /me/invitations item shape. */
export interface MyInvitationDto {
  id: string
  clinicId: string
  clinicName: string
  role: StaffRole
  scope: MembershipScope
  branchNames: string[]
  expiresAt: string
}

// ---------------------------------------------------------------- appointments

export interface AppointmentHistoryEntry {
  from: AppointmentStatus | null
  to: AppointmentStatus
  at: string
  actor: 'Patient' | 'Staff' | 'System'
  reasonCode: string | null
}

export interface AppointmentDto {
  id: string
  clinicId: string
  clinicName: string
  branchId: string
  branchName: string
  timeZone: string
  serviceId: string
  serviceName: string
  startsAt: string
  endsAt: string
  status: AppointmentStatus
  expiresAt: string | null
  quotedPrice: Money
  chargedAmount: Money | null
  doctor: { id: string; name: string } | null
  contactName: string
  contactPhone: string
  source: 'Patient' | 'WalkIn'
  canCancel: boolean
  createdAt: string
  history?: AppointmentHistoryEntry[]
}

export interface CreateAppointmentBody {
  branchId: string
  serviceId: string
  startsAt: string
  contactName: string
  contactPhone: string
}

export interface ReviewBody {
  rating: number
  text: string
}

export interface ReportBody {
  targetType: 'Clinic' | 'Review'
  targetId: string
  reason: string
}

// ---------------------------------------------------------------- public catalogue

export type ClinicSort = 'rating' | 'price' | 'distance' | 'name'

export interface ClinicSearchParams {
  q?: string
  serviceCategory?: string
  serviceId?: string
  district?: string
  lat?: number
  lng?: number
  availableWithinDays?: number
  sort?: ClinicSort
  page?: number
  pageSize?: number
}

export interface ClinicCard {
  id: string
  name: string
  rating: number | null
  reviewCount: number
  minPrice: Money | null
  branchCount: number
  districts: string[]
  distanceKm: number | null
  nextFreeSlot: string | null
}

export type PriceKind = 'Fixed' | 'From'

export interface OpeningHours {
  weekday: Weekday
  open: string
  close: string
}

export interface PublicServiceOffer {
  serviceId: string
  name: string
  category: string
  price: Money
  priceKind: PriceKind
  durationMinutes: number
}

export interface PublicBranch {
  id: string
  name: string
  district: string
  address: string
  latitude: number | null
  longitude: number | null
  timeZone: string
  phone: string
  hours: OpeningHours[]
  services: PublicServiceOffer[]
}

export interface ClinicDetail {
  id: string
  name: string
  description: string
  rating: number | null
  reviewCount: number
  branches: PublicBranch[]
}

export interface ReviewDto {
  id: string
  rating: number
  text: string
  authorName: string
  createdAt: string
  branchName: string
}

export interface Availability {
  date: string
  timeZone: string
  durationMinutes: number
  slots: { startsAt: string; localTime: string }[]
}

export interface Facets {
  districts: string[]
  categories: { key: string; name: string }[]
  services: { id: string; name: string; category: string }[]
}

// ---------------------------------------------------------------- clinic console

export interface RegisterClinicBody {
  name: string
  legalName: string
  description: string
  phone: string
}

/** ASSUMED: GET /clinics/{id} shape (fields of POST /clinics plus status). */
export interface ClinicProfileDto {
  id: string
  name: string
  legalName: string
  description: string
  phone: string
  verificationStatus: ClinicVerification
  createdAt: string
}

/** ASSUMED: dashboard counters; `collected` present only with finance.report.view. */
export interface DashboardDto {
  date: string
  pending: number
  confirmed: number
  completed: number
  noShow: number
  collected?: Money | null
}

/** ASSUMED: branch shape (API.md lists the fields but not the response object). */
export interface BranchDto {
  id: string
  name: string
  district: string
  address: string
  latitude: number | null
  longitude: number | null
  phone: string
  timeZone: string
  isActive: boolean
  hours: OpeningHours[]
}

export type BranchBody = Omit<BranchDto, 'id' | 'hours'>

/** ASSUMED: branch offers are embedded in the service list (needed for the price editor). */
export interface BranchOfferDto {
  branchId: string
  price: Money
  priceKind: PriceKind
  durationMinutes: number
  isActive: boolean
}

export interface ServiceDto {
  id: string
  name: string
  category: string
  isActive: boolean
  offers: BranchOfferDto[]
}

export interface ServiceBody {
  name: string
  category: string
  isActive: boolean
}

export interface BranchOfferBody {
  price: Money
  priceKind: PriceKind
  durationMinutes: number
  isActive: boolean
}

/** ASSUMED: GET /staff item. */
export interface StaffMemberDto {
  membershipId: string
  userId: string
  displayName: string
  phoneMasked: string | null
  role: StaffRole
  scope: MembershipScope
  branchIds: string[]
  status: 'Active' | 'Suspended'
}

export interface StaffBody {
  role: StaffRole
  scope: MembershipScope
  branchIds: string[]
}

/** ASSUMED: GET /invitations item. */
export interface InvitationDto {
  id: string
  phoneMasked: string
  role: StaffRole
  scope: MembershipScope
  branchIds: string[]
  createdAt: string
  expiresAt: string
}

export interface InvitationBody extends StaffBody {
  phone: string
}

/** ASSUMED: doctor shape, mirroring the PUT body. */
export interface DoctorDto {
  id: string
  displayName: string
  isActive: boolean
  branchIds: string[]
  serviceIds: string[]
  userId: string | null
}

export type DoctorBody = Omit<DoctorDto, 'id'>

export interface WorkingHoursEntry {
  branchId: string
  weekday: Weekday
  start: string
  end: string
}

/** ASSUMED: time-off item. */
export interface TimeOffDto {
  id: string
  startsAt: string
  endsAt: string
  reason: string | null
}

export interface ClinicAppointmentFilters {
  branchId?: string
  status?: AppointmentStatus
  from?: string
  to?: string
  doctorId?: string
  page?: number
  pageSize?: number
}

/** ASSUMED: eligible doctor item. */
export interface EligibleDoctor {
  id: string
  name: string
}

export interface WalkInBody {
  branchId: string
  serviceId: string
  startsAt: string
  doctorId: string
  contactName: string
  contactPhone: string
}

/** ASSUMED: calendar day view. */
export interface CalendarBlock {
  kind: 'Appointment' | 'Hold' | 'TimeOff'
  startsAt: string
  endsAt: string
  appointmentId: string | null
  status: AppointmentStatus | null
  serviceName: string | null
  contactName: string | null
}

export interface CalendarDto {
  date: string
  timeZone: string
  doctors: { id: string; name: string; blocks: CalendarBlock[] }[]
}

// ---------------------------------------------------------------- finance

export type PaymentMethod = 'Cash' | 'Card' | 'Transfer'
export const paymentMethods: readonly PaymentMethod[] = ['Cash', 'Card', 'Transfer']

/** ASSUMED: payment list item. */
export interface PaymentDto {
  id: string
  appointmentId: string
  branchId: string
  direction: 'In' | 'Refund'
  method: PaymentMethod
  amount: Money
  status: 'Recorded' | 'Failed' | 'Voided'
  externalRef: string | null
  refundOfPaymentId: string | null
  recordedAt: string
  contactName: string | null
  serviceName: string | null
}

export interface Balance {
  charged: Money
  paid: Money
  refunded: Money
  outstanding: Money
}

export interface RecordPaymentBody {
  amount: Money
  method: PaymentMethod
  externalRef?: string
}

export interface RefundBody {
  amount: Money
  reason: string
}

/** ASSUMED: daily report row. */
export interface DailyReportRow {
  date: string
  branchId: string | null
  visits: number
  charged: Money
  collected: Money
  refunded: Money
  commission: Money
}

/** ASSUMED: monthly report. */
export interface MonthlyReport {
  year: number
  month: number
  visits: number
  charged: Money
  collected: Money
  refunded: Money
  commission: Money
  reconciled: boolean
  commissionRuleConfigured: boolean
}

export type SettlementStatus = 'Draft' | 'Finalized' | 'Paid' | 'Voided'
export const settlementStatuses: readonly SettlementStatus[] = ['Draft', 'Finalized', 'Paid', 'Voided']

/** ASSUMED: settlement shape. */
export interface SettlementDto {
  id: string
  clinicId: string
  clinicName: string
  year: number
  month: number
  status: SettlementStatus
  amountDue: Money
  createdAt: string
  finalizedAt: string | null
  paidAt: string | null
  paidReference: string | null
  voidReason: string | null
}

/** ASSUMED: audit entry. */
export interface AuditEntryDto {
  id: string
  occurredAt: string
  actorName: string | null
  action: string
  clinicId: string | null
  branchId: string | null
  targetType: string | null
  targetId: string | null
}

// ---------------------------------------------------------------- platform admin

/** ASSUMED */
export interface AdminDashboardDto {
  clinicsByStatus: Partial<Record<ClinicVerification, number>>
  appointmentsToday: number
  openReports: number
}

/** ASSUMED */
export interface AdminClinicDto {
  id: string
  name: string
  legalName: string
  phone: string
  verificationStatus: ClinicVerification
  isSuspended?: boolean
  branchCount: number
  createdAt: string
}

/** ASSUMED */
export interface AdminUserDto {
  id: string
  displayName: string
  phoneMasked: string | null
  email: string | null
  status: 'Active' | 'Suspended'
  createdAt: string
}

export type ReportStatus = 'Open' | 'Resolved'

/** ASSUMED */
export interface AbuseReportDto {
  id: string
  targetType: 'Clinic' | 'Review'
  targetId: string
  targetLabel: string | null
  reason: string
  status: ReportStatus
  createdAt: string
  resolution: string | null
}

export type ReviewModerationStatus = 'Pending' | 'Published' | 'Hidden'

/** ASSUMED */
export interface AdminReviewDto {
  id: string
  clinicId: string
  clinicName: string
  rating: number
  text: string
  authorName: string
  status: ReviewModerationStatus
  createdAt: string
}

export type CommissionBasis = 'CollectedPayment' | 'CompletedService' | 'ConfirmedAppointment' | 'NewPatient'
export const commissionBases: readonly CommissionBasis[] = ['CollectedPayment', 'CompletedService', 'ConfirmedAppointment', 'NewPatient']
/** Percentage bases carry `rate`; fixed-fee bases carry `fixedFee` (DECISIONS.md OD-F1). */
export const rateBases: readonly CommissionBasis[] = ['CollectedPayment', 'CompletedService']

/** ASSUMED: rate is a decimal fraction string ("0.1000" = 10 %), matching rate_snapshot. */
export interface CommissionRuleDto {
  id: string
  basis: CommissionBasis
  rate: string | null
  fixedFee: Money | null
  effectiveFrom: string
  effectiveTo: string | null
  contractReference: string
  createdAt: string
}

export interface CommissionRuleBody {
  basis: CommissionBasis
  rate?: string
  fixedFee?: Money
  effectiveFrom: string
  effectiveTo?: string
  contractReference: string
}

export type NotificationStatus = 'Pending' | 'Sent' | 'Failed'
export const notificationStatuses: readonly NotificationStatus[] = ['Pending', 'Sent', 'Failed']

/** ASSUMED */
export interface NotificationDto {
  id: string
  type: string
  channel: string
  status: NotificationStatus
  attempts: number
  createdAt: string
  nextAttemptAt: string | null
  lastError: string | null
}
