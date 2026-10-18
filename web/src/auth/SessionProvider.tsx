import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useApi } from '../api/context'
import type { Me } from '../api/types'
import { SessionContext, type SessionStatus, type SessionValue } from './session'

export function SessionProvider({ children }: { children: ReactNode }) {
  const api = useApi()
  const [me, setMeState] = useState<Me | null>(null)
  const [status, setStatus] = useState<SessionStatus>('loading')

  const setMe = useCallback((value: Me | null) => {
    setMeState(value)
    setStatus(value ? 'authenticated' : 'anonymous')
  }, [])

  const refresh = useCallback(async () => {
    try {
      setMe(await api.me.get())
    } catch {
      // 401 = not signed in. Any other failure also leaves the visitor anonymous; protected
      // screens then send them to the login page, where errors are shown properly.
      setMe(null)
    }
  }, [api, setMe])

  useEffect(() => {
    let alive = true
    api.me.get().then(
      (value) => alive && setMe(value),
      () => alive && setMe(null),
    )
    return () => {
      alive = false
    }
  }, [api, setMe])

  useEffect(() => api.client.onUnauthenticated(() => setMe(null)), [api, setMe])

  const signOut = useCallback(
    async (everywhere = false) => {
      try {
        await (everywhere ? api.auth.logoutAll() : api.auth.logout())
      } finally {
        setMe(null)
      }
    },
    [api, setMe],
  )

  const value = useMemo<SessionValue>(() => ({ status, me, refresh, setMe, signOut }), [status, me, refresh, setMe, signOut])
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}
