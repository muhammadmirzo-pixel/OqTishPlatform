import { createContext, useContext } from 'react'
import type { StaffRole } from '../../domain/types'

export interface DemoRoleValue {
  role: StaffRole
  setRole: (role: StaffRole) => void
}

export const DemoRoleContext = createContext<DemoRoleValue>({ role: 'ClinicOwner', setRole: () => undefined })

export function useDemoRole(): DemoRoleValue {
  return useContext(DemoRoleContext)
}
