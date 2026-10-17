import type { StaffRole } from '../../domain/types'

export type ClinicSection = 'dashboard' | 'requests' | 'calendar' | 'doctors' | 'services' | 'staff' | 'payments' | 'reports'

/**
 * Which console sections each role sees (docs/SECURITY.md §2.2, decisions of 2026-10-18).
 * This only shapes the MENU. The API enforces the same rules per request and per resource;
 * hiding a link is never a security control.
 */
export const sectionsByRole: Record<StaffRole, readonly ClinicSection[]> = {
  ClinicOwner: ['dashboard', 'requests', 'calendar', 'doctors', 'services', 'staff', 'payments', 'reports'],
  ClinicAdmin: ['dashboard', 'requests', 'calendar', 'doctors', 'services', 'staff'],
  Receptionist: ['dashboard', 'requests', 'calendar', 'doctors', 'payments'],
  Doctor: ['dashboard', 'calendar'],
  FinanceManager: ['dashboard', 'payments', 'reports'],
}

export function canSee(role: StaffRole, section: ClinicSection): boolean {
  return sectionsByRole[role].includes(section)
}
