import type { ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { hasAny } from '../features/clinic/permissions'
import { useClinic } from '../features/clinic/clinicContext'
import { ForbiddenState, LoadingState, TotpRequiredState } from '../ui/states'
import { loginPath, useSession } from './session'

/** Unauthenticated visitors are sent to /login?next=<current path>. */
export function RequireAuth({ children }: { children?: ReactNode }) {
  const { status } = useSession()
  const location = useLocation()
  if (status === 'loading') return <LoadingState />
  if (status === 'anonymous') return <Navigate replace to={loginPath(location.pathname + location.search)} />
  return <>{children ?? <Outlet />}</>
}

/** Platform admin area: requires at least one platform permission (server re-checks each call). */
export function RequirePlatform({ children }: { children?: ReactNode }) {
  const { me } = useSession()
  if (!me || me.platformPermissions.length === 0) {
    // Effective platform permissions are [] until TOTP is enabled (docs/API.md §0, BE1a).
    return <div className="page narrow">{me?.totpSetupRequired ? <TotpRequiredState /> : <ForbiddenState />}</div>
  }
  return <>{children ?? <Outlet />}</>
}

export function RequirePlatformPermission({ anyOf, children }: { anyOf: readonly string[]; children: ReactNode }) {
  const { me } = useSession()
  return hasAny(me?.platformPermissions ?? [], anyOf) ? <>{children}</> : <ForbiddenState />
}

/** Clinic console section guard, driven by the membership's effective permissions. */
export function RequirePermission({ anyOf, children }: { anyOf: readonly string[]; children: ReactNode }) {
  const { can } = useClinic()
  return can(...anyOf) ? <>{children}</> : <ForbiddenState />
}
