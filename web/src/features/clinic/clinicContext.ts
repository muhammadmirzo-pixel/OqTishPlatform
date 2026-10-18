import { createContext, useContext } from 'react'
import type { BranchDto, Membership } from '../../api/types'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { hasAny } from './permissions'

export interface ClinicContextValue {
  clinicId: string
  membership: Membership
  /** Branches visible to this member (Branches-scoped members only see their own). */
  branches: BranchDto[]
  reloadBranches: () => void
  can: (...anyOf: string[]) => boolean
  /**
   * Like `can`, but only for a clinic-wide membership: actions about the whole clinic (creating a
   * branch, creating/editing a service, editing the profile) need the permission clinic-wide
   * (DECISIONS.md BE1a-8, BE1b-2). The server re-checks.
   */
  canClinicWide: (...anyOf: string[]) => boolean
  branchName: (branchId: string) => string
  /** Time zone of the branch, or of the first branch when none is selected. */
  timeZoneFor: (branchId?: string | null) => string
}

export const ClinicContext = createContext<ClinicContextValue | null>(null)

export function useClinic(): ClinicContextValue {
  const value = useContext(ClinicContext)
  if (!value) throw new Error('useClinic must be used inside the clinic console')
  return value
}

export function makeClinicContext(membership: Membership, allBranches: BranchDto[], reloadBranches: () => void): ClinicContextValue {
  const branches = membership.scope === 'Branches' ? allBranches.filter((b) => membership.branchIds.includes(b.id)) : allBranches
  return {
    clinicId: membership.clinicId,
    membership,
    branches,
    reloadBranches,
    can: (...anyOf) => hasAny(membership.permissions, anyOf),
    canClinicWide: (...anyOf) => membership.scope === 'Clinic' && hasAny(membership.permissions, anyOf),
    branchName: (id) => allBranches.find((b) => b.id === id)?.name ?? '—',
    timeZoneFor: (id) => (id ? allBranches.find((b) => b.id === id)?.timeZone : undefined) ?? branches[0]?.timeZone ?? DEFAULT_TIME_ZONE,
  }
}
