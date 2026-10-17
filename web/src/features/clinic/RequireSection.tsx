import type { ReactNode } from 'react'
import { ForbiddenState } from '../../ui/states'
import { canSee, type ClinicSection } from './permissions'
import { useDemoRole } from './roleContext'

/** Renders the permission-denied state when the (demo) role may not open this section. */
export function RequireSection({ section, children }: { section: ClinicSection; children: ReactNode }) {
  const { role } = useDemoRole()
  return canSee(role, section) ? children : <ForbiddenState />
}
