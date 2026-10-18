import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'

export interface QueryState<T> {
  data: T | undefined
  error: unknown
  loading: boolean
  /** Re-runs the request; keeps showing the previous data meanwhile. */
  reload: () => void
  setData: (updater: T | ((previous: T | undefined) => T)) => void
}

interface Settled<T> {
  key: string
  request: string
  data: T | undefined
  error: unknown
}

/**
 * Minimal server-state hook: runs `fetcher` whenever `key` changes, ignores out-of-order responses
 * and exposes reload. `key === null` disables the request. `refreshMs` polls while mounted.
 * Loading is derived (the latest request has not settled yet), so effects never set state
 * synchronously; state changes only happen when a response arrives.
 */
export function useQuery<T>(key: string | null, fetcher: () => Promise<T>, options: { refreshMs?: number } = {}): QueryState<T> {
  const [nonce, setNonce] = useState(0)
  const [settled, setSettled] = useState<Settled<T> | null>(null)
  const fetcherRef = useRef(fetcher)
  useLayoutEffect(() => {
    fetcherRef.current = fetcher
  })
  const request = key === null ? null : `${key}#${nonce}`

  useEffect(() => {
    if (key === null || request === null) return
    let current = true
    fetcherRef.current().then(
      (data) => current && setSettled({ key, request, data, error: undefined }),
      (error: unknown) => current && setSettled((prev) => ({ key, request, data: prev?.key === key ? prev.data : undefined, error })),
    )
    return () => {
      current = false
    }
  }, [key, request])

  const refreshMs = options.refreshMs
  useEffect(() => {
    if (!refreshMs || key === null) return
    const timer = setInterval(() => setNonce((n) => n + 1), refreshMs)
    return () => clearInterval(timer)
  }, [refreshMs, key])

  const reload = useCallback(() => setNonce((n) => n + 1), [])
  const setData = useCallback(
    (updater: T | ((previous: T | undefined) => T)) => {
      setSettled((prev) => {
        const base = prev ?? { key: key ?? '', request: request ?? '', data: undefined, error: undefined }
        const data = typeof updater === 'function' ? (updater as (p: T | undefined) => T)(base.data) : updater
        return { ...base, data, error: undefined }
      })
    },
    [key, request],
  )

  const sameKey = settled !== null && settled.key === key
  const fresh = sameKey && settled.request === request
  return {
    data: sameKey ? settled.data : undefined,
    error: fresh ? settled.error : undefined,
    loading: request !== null && !fresh,
    reload,
    setData,
  }
}
