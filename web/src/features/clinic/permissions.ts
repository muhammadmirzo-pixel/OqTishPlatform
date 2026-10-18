import type { MessageKey } from '../../i18n/messages'
import type { StaffRole } from '../../api/types'

/**
 * Permission names from docs/SECURITY.md §8. Menus and buttons are filtered by the membership's
 * effective `permissions` array from GET /api/me — never by role names. This only shapes the UI;
 * the API enforces the same rules per request and per resource. Hiding a link is not security.
 */
export const Perm = {
  profileManage: 'clinic.profile.manage',
  branchManage: 'branch.manage',
  staffManage: 'staff.manage',
  catalogManage: 'catalog.manage',
  scheduleManage: 'schedule.manage',
  ownTimeOff: 'schedule.own_timeoff',
  appointmentView: 'appointment.view',
  triage: 'appointment.triage',
  cancel: 'appointment.cancel',
  outcome: 'appointment.outcome',
  walkIn: 'appointment.create_walkin',
  paymentRecord: 'payment.record',
  paymentRefund: 'payment.refund',
  reportView: 'finance.report.view',
  settlementView: 'finance.settlement.view',
  auditView: 'audit.view',
} as const

export const PlatformPerm = {
  clinicVerify: 'platform.clinic.verify',
  accountSuspend: 'platform.account.suspend',
  reportsHandle: 'platform.reports.handle',
  financeSettle: 'platform.finance.settle',
  tenantRead: 'platform.tenant-data.read',
  auditView: 'platform.audit.view',
} as const

/** True when `perms` holds at least one of `anyOf` (an empty list means "any member"). */
export function hasAny(perms: readonly string[], anyOf: readonly string[]): boolean {
  return anyOf.length === 0 || anyOf.some((p) => perms.includes(p))
}

export interface MenuItem {
  /** Path relative to the console root ("" = dashboard). */
  path: string
  label: MessageKey
  anyOf: readonly string[]
}

export const clinicMenu: readonly MenuItem[] = [
  { path: '', label: 'clinicNav.dashboard', anyOf: [] },
  { path: 'requests', label: 'clinicNav.requests', anyOf: [Perm.triage] },
  { path: 'appointments', label: 'clinicNav.appointments', anyOf: [Perm.appointmentView] },
  { path: 'calendar', label: 'clinicNav.calendar', anyOf: [Perm.appointmentView] },
  { path: 'walk-in', label: 'clinicNav.walkIn', anyOf: [Perm.walkIn] },
  { path: 'doctors', label: 'clinicNav.doctors', anyOf: [Perm.scheduleManage, Perm.ownTimeOff, Perm.catalogManage] },
  { path: 'services', label: 'clinicNav.services', anyOf: [Perm.catalogManage] },
  { path: 'branches', label: 'clinicNav.branches', anyOf: [Perm.branchManage] },
  { path: 'staff', label: 'clinicNav.staff', anyOf: [Perm.staffManage] },
  { path: 'payments', label: 'clinicNav.payments', anyOf: [Perm.paymentRecord, Perm.reportView] },
  { path: 'reports', label: 'clinicNav.reports', anyOf: [Perm.reportView] },
  { path: 'settlements', label: 'clinicNav.settlements', anyOf: [Perm.settlementView] },
  { path: 'audit', label: 'clinicNav.audit', anyOf: [Perm.auditView] },
  { path: 'profile', label: 'clinicNav.profile', anyOf: [] },
]

export const adminMenu: readonly MenuItem[] = [
  { path: '', label: 'adminNav.dashboard', anyOf: [] },
  { path: 'clinics', label: 'adminNav.clinics', anyOf: [PlatformPerm.clinicVerify] },
  { path: 'users', label: 'adminNav.users', anyOf: [PlatformPerm.accountSuspend] },
  { path: 'reports', label: 'adminNav.reports', anyOf: [PlatformPerm.reportsHandle] },
  { path: 'reviews', label: 'adminNav.reviews', anyOf: [PlatformPerm.reportsHandle] },
  { path: 'commission', label: 'adminNav.commission', anyOf: [PlatformPerm.financeSettle] },
  { path: 'settlements', label: 'adminNav.settlements', anyOf: [PlatformPerm.financeSettle] },
  { path: 'tenant-data', label: 'adminNav.tenantData', anyOf: [PlatformPerm.tenantRead] },
  { path: 'audit', label: 'adminNav.audit', anyOf: [PlatformPerm.auditView] },
  { path: 'notifications', label: 'adminNav.notifications', anyOf: [PlatformPerm.auditView] },
]

export function visibleMenu(menu: readonly MenuItem[], perms: readonly string[]): MenuItem[] {
  return menu.filter((item) => hasAny(perms, item.anyOf))
}

/**
 * Roles a member may grant when inviting or editing staff (SECURITY.md §8: only owners grant
 * ClinicOwner/ClinicAdmin). The server re-checks and answers 403 staff.cannot_grant_role.
 */
export function grantableRoles(myRole: StaffRole): StaffRole[] {
  const all: StaffRole[] = ['ClinicOwner', 'ClinicAdmin', 'Receptionist', 'Doctor', 'FinanceManager']
  return myRole === 'ClinicOwner' ? all : all.filter((r) => r !== 'ClinicOwner' && r !== 'ClinicAdmin')
}

/** Roles that need TOTP before their permissions take effect (SECURITY.md §8). */
export function roleNeedsTotp(role: StaffRole): boolean {
  return role === 'ClinicOwner' || role === 'FinanceManager'
}
