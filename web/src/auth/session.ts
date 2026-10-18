import { createContext, useContext } from 'react'
import type { Me, Membership } from '../api/types'

export type SessionStatus = 'loading' | 'anonymous' | 'authenticated'

export interface SessionValue {
  status: SessionStatus
  me: Me | null
  /** Re-reads GET /me (after linking a phone, accepting an invitation, registering a clinic …). */
  refresh: () => Promise<void>
  setMe: (me: Me | null) => void
  signOut: (everywhere?: boolean) => Promise<void>
}

export const SessionContext = createContext<SessionValue | null>(null)

export function useSession(): SessionValue {
  const value = useContext(SessionContext)
  if (!value) throw new Error('useSession must be used inside <SessionProvider>')
  return value
}

export function findMembership(me: Me | null, clinicId: string | undefined): Membership | undefined {
  return clinicId ? me?.memberships.find((m) => m.clinicId === clinicId) : undefined
}

/** Only same-site relative paths are accepted as post-login targets (no open redirects). */
export function safeNext(next: string | null | undefined): string | null {
  if (!next || !next.startsWith('/') || next.startsWith('//') || next.startsWith('/\\')) return null
  return next
}

export function landingFor(me: Me): string {
  if (me.memberships.length > 0) return '/clinic'
  if (me.platformPermissions.length > 0) return '/admin'
  return '/'
}

export function loginPath(next: string): string {
  return `/login?next=${encodeURIComponent(next)}`
}
