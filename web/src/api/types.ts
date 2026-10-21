// Types mirroring the DTOs in docs/API.md; all shapes follow the implemented backend (BE1-BE4).
// The server stays authoritative for every value.

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
  /** Clinic logo image id (GET /files/images/{id}); null = monogram. */
  clinicLogoId?: string | null
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
  /** Profile photo image id; null = initials. */
  avatarId?: string | null
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
   * The bot's numeric id (public; the part of the token before ":"), null while Telegram is off. With it
   * the website uses Telegram.Login.auth() (popup, no eval); without it the widget's redirect mode is used.
   */
  botId: number | null
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

/** GET /me/invitations item (API.md §4.1). */
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
  priceKind: PriceKind
  chargedAmount: Money | null
  doctor: { id: string; name: string } | null
  contactName: string
  contactPhone: string
  source: 'Patient' | 'WalkIn'
  canCancel: boolean
  /** The patient already left a review for this appointment. */
  hasReview: boolean
  /** Completed, own, inside the review window and not yet reviewed (server decides). */
  canReview: boolean
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
  logoId?: string | null
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
  logoId?: string | null
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

/** GET /public/facets (API.md §2.1). Category `name` is the Uzbek default — localise by `key`. */
export interface Facets {
  districts: string[]
  categories: { key: string; name: string }[]
  services: { id: string; name: string; category: string }[]
}

// ---------------------------------------------------------------- clinic console

/** POST/PUT /clinics body: `name` 1–200; legalName ≤ 300, description ≤ 4000, phone — optional ("" = not set). */
export interface RegisterClinicBody {
  name: string
  legalName: string
  description: string
  phone: string
}

/** GET /clinics/{id} (API.md §4.1). Empty strings mean "not set". */
export interface ClinicProfileDto {
  id: string
  name: string
  logoId?: string | null
  legalName: string
  description: string
  phone: string
  verificationStatus: ClinicVerification
  /** Reason of the last reject/suspend, shown to the owner. */
  statusReason: string | null
  createdAt: string
}

/** GET /clinics/{id}/dashboard (API.md §4.1); `collected` is null without finance.report.view. */
export interface DashboardDto {
  /** YYYY-MM-DD, branch/clinic local. */
  date: string
  branchId: string | null
  pending: number
  confirmed: number
  completed: number
  noShow: number
  collected: Money | null
}

/** Branch time zones accepted by the API (DECISIONS.md BE1b-15). */
export const branchTimeZones = ['Asia/Tashkent', 'Asia/Samarkand'] as const
export type BranchTimeZone = (typeof branchTimeZones)[number]

/** Fixed service category keys (API.md §2.1, DECISIONS.md BE1b-1). Labels: `category.<key>`. */
export const serviceCategories = [
  'diagnostics',
  'hygiene',
  'therapy',
  'surgery',
  'implants',
  'prosthetics',
  'orthodontics',
  'pediatric',
  'whitening',
  'periodontics',
  'other',
] as const
export type ServiceCategory = (typeof serviceCategories)[number]

/** Branch (API.md §4.1). `phone` is "" when not set. */
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

/** POST/PUT branch body; coordinates both or neither (≤ 6 decimals), timeZone from the allow-list. */
export type BranchBody = Omit<BranchDto, 'id' | 'hours'>

/** Branch offer embedded in ServiceDto (only branches in the caller's scope). */
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
  category: ServiceCategory
  isActive: boolean
}

export interface BranchOfferBody {
  price: Money
  priceKind: PriceKind
  durationMinutes: number
  isActive: boolean
}

/** GET /staff item (API.md §4.1). */
export interface StaffMemberDto {
  membershipId: string
  userId: string
  displayName: string
  /** Profile photo image id; null = initials. */
  avatarId?: string | null
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

/** GET /invitations item (API.md §4.1); expired ones are included — compare `expiresAt`. */
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

/** Doctor (API.md §4.1). */
export interface DoctorDto {
  id: string
  displayName: string
  /** Profile photo of the linked user, if any. */
  avatarId?: string | null
  isActive: boolean
  branchIds: string[]
  serviceIds: string[]
  userId: string | null
}

/**
 * POST/PUT doctor body. Links are replaced. Changing `userId` needs `staff.manage` as well, so the
 * field is sent only when the caller may (and wants to) change it.
 */
export interface DoctorBody {
  displayName: string
  isActive: boolean
  branchIds: string[]
  serviceIds: string[]
  userId?: string | null
}

export interface WorkingHoursEntry {
  branchId: string
  weekday: Weekday
  start: string
  end: string
}

/** Doctor time off (API.md §4.2). */
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

/** GET …/eligible-doctors item (API.md §4.2). */
export interface EligibleDoctor {
  id: string
  name: string
  /** Has working hours covering the interval; false = staff overrides the schedule. */
  withinWorkingHours: boolean
  /** The doctor currently holds this (Pending) request. */
  provisional: boolean
}

export interface WalkInBody {
  branchId: string
  serviceId: string
  startsAt: string
  doctorId: string
  contactName: string
  contactPhone: string
}

/** Calendar block (API.md §4.2). `otherBranch` = busy at another branch (no details). */
export interface CalendarBlock {
  kind: 'Appointment' | 'Hold' | 'TimeOff'
  startsAt: string
  endsAt: string
  appointmentId: string | null
  status: AppointmentStatus | null
  serviceName: string | null
  contactName: string | null
  otherBranch: boolean
  /** Time-off id (for deletion). */
  blockId: string | null
}

export interface CalendarDoctor {
  id: string
  name: string
  workingHours: { start: string; end: string }[]
  blocks: CalendarBlock[]
}

export interface CalendarDto {
  date: string
  timeZone: string
  branchId: string
  doctors: CalendarDoctor[]
}

// ---------------------------------------------------------------- finance

export type PaymentMethod = 'Cash' | 'Card' | 'Transfer'
export const paymentMethods: readonly PaymentMethod[] = ['Cash', 'Card', 'Transfer']

/** Payment (API.md §4.3). */
export interface PaymentDto {
  id: string
  appointmentId: string
  branchId: string
  direction: 'In' | 'Refund'
  method: PaymentMethod
  amount: Money
  status: 'Recorded' | 'Failed'
  externalRef: string | null
  refundOfPaymentId: string | null
  recordedAt: string
  contactName: string | null
  serviceName: string | null
  /** Sum of refunds of an incoming payment. */
  refunded: Money
  /** What can still be refunded. */
  refundable: Money
  reason: string | null
  failureReason: string | null
  failedAt: string | null
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
  /** 'Failed' records a declined attempt without ledger effect. */
  status?: 'Failed'
}

export interface RefundBody {
  amount: Money
  reason: string
}

/** Daily report row (API.md §4.3). */
export interface DailyReportRow {
  date: string
  branchId: string | null
  visits: number
  charged: Money
  collected: Money
  refunded: Money
  commission: Money
  /** Refunds of payments posted in an earlier month. */
  priorPeriodRefunded: Money
}

/** Monthly report (API.md §4.3). */
export interface MonthlyReport {
  year: number
  month: number
  branchId: string | null
  visits: number
  charged: Money
  collected: Money
  refunded: Money
  commission: Money
  priorPeriodRefunded: Money
  reconciled: boolean
  commissionRuleConfigured: boolean
  /** The month is locked by a Finalized/Paid settlement. */
  locked: boolean
}

export type SettlementStatus = 'Draft' | 'Finalized' | 'Paid' | 'Voided'
export const settlementStatuses: readonly SettlementStatus[] = ['Draft', 'Finalized', 'Paid', 'Voided']

/** Settlement (API.md §4.3 / §5.4); `periodEnd` is exclusive. */
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
  periodStart: string
  periodEnd: string
  itemCount: number
  commissionRuleConfigured: boolean
  paymentTerms: string | null
}

export type AuditActorKind = 'User' | 'System' | 'Anonymous'

/** Audit entry, clinic and platform (API.md §4.1). */
export interface AuditEntryDto {
  id: string
  occurredAt: string
  actorKind: AuditActorKind
  actorUserId: string | null
  actorName: string | null
  action: string
  clinicId: string | null
  branchId: string | null
  targetType: string | null
  targetId: string | null
}

// ---------------------------------------------------------------- platform admin

/** GET /admin/dashboard (API.md §5.1). `appointmentsToday` / `openReports` are 0 until BE2. */
export interface AdminDashboardDto {
  clinicsByStatus: Partial<Record<ClinicVerification, number>>
  appointmentsToday: number
  openReports: number
  activeUsers: number
  suspendedUsers: number
}

/** GET /admin/clinics item; approve/reject/suspend/reinstate answer 200 with this (API.md §5.1). */
export interface AdminClinicDto {
  id: string
  name: string
  legalName: string
  phone: string
  verificationStatus: ClinicVerification
  isSuspended: boolean
  statusReason: string | null
  branchCount: number
  createdAt: string
}

/** GET /admin/users item (API.md §5.1). */
export interface AdminUserDto {
  id: string
  displayName: string
  phoneMasked: string | null
  email: string | null
  status: 'Active' | 'Suspended'
  statusReason: string | null
  createdAt: string
}

export type ReportStatus = 'Open' | 'Resolved'

/** Abuse report (API.md §5.3). */
export interface AbuseReportDto {
  id: string
  targetType: 'Clinic' | 'Review'
  targetId: string
  targetLabel: string | null
  clinicId: string
  clinicName: string
  reason: string
  status: ReportStatus
  createdAt: string
  resolution: string | null
  resolvedAt: string | null
}

export type ReviewModerationStatus = 'Pending' | 'Published' | 'Hidden'

/** Admin review row (API.md §5.3). */
export interface AdminReviewDto {
  id: string
  clinicId: string
  clinicName: string
  branchName: string
  appointmentId: string
  rating: number
  text: string
  authorName: string
  status: ReviewModerationStatus
  moderationReason: string | null
  createdAt: string
}

export type CommissionBasis = 'CollectedPayment' | 'CompletedService' | 'ConfirmedAppointment' | 'NewPatient'
export const commissionBases: readonly CommissionBasis[] = ['CollectedPayment', 'CompletedService', 'ConfirmedAppointment', 'NewPatient']
/** Percentage bases carry `rate`; fixed-fee bases carry `fixedFee` (DECISIONS.md OD-F1). */
export const rateBases: readonly CommissionBasis[] = ['CollectedPayment', 'CompletedService']

/** Rate is a fraction string with 4 decimals ("0.0750" = 7.5 %). */
export interface CommissionRuleDto {
  id: string
  clinicId: string
  basis: CommissionBasis
  rate: string | null
  fixedFee: Money | null
  effectiveFrom: string
  effectiveTo: string | null
  contractReference: string
  rounding: 'AwayFromZero'
  approvedByUserId: string
  createdAt: string
  endedAt: string | null
}

export interface CommissionRuleBody {
  basis: CommissionBasis
  rate?: string
  fixedFee?: Money
  effectiveFrom: string
  effectiveTo?: string
  contractReference: string
}

export type NotificationStatus = 'Pending' | 'Processing' | 'Sent' | 'Failed' | 'Skipped' | 'Cancelled'
export const notificationStatuses: readonly NotificationStatus[] = ['Pending', 'Processing', 'Sent', 'Failed', 'Skipped', 'Cancelled']

/** GET /admin/notifications item (API.md §5.5); never carries the message text. */
export interface NotificationDto {
  id: string
  type: string
  audience: 'patient' | 'staff'
  /** 'Telegram' | 'Sms' | 'Telegram,Sms' of the last attempt. */
  channel: string | null
  status: NotificationStatus
  attempts: number
  createdAt: string
  notBefore: string
  nextAttemptAt: string | null
  sentAt: string | null
  lastErrorCode: string | null
  lastError: string | null
}
