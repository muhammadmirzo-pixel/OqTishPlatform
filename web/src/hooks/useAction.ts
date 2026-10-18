import { useCallback, useLayoutEffect, useRef, useState } from 'react'

export type Outcome<R> = { ok: true; value: R } | { ok: false; error: unknown }

export interface ActionState<A extends unknown[], R> {
  run: (...args: A) => Promise<Outcome<R>>
  pending: boolean
  error: unknown
  clearError: () => void
}

const BUSY: Outcome<never> = { ok: false, error: undefined }

/**
 * Wraps a mutation: tracks pending/error and prevents double submission. The returned outcome
 * tells the caller whether it succeeded; the error is also kept in state for display.
 */
export function useAction<A extends unknown[], R>(action: (...args: A) => Promise<R>): ActionState<A, R> {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<unknown>(undefined)
  const busy = useRef(false)
  const actionRef = useRef(action)
  useLayoutEffect(() => {
    actionRef.current = action
  })

  const run = useCallback(async (...args: A): Promise<Outcome<R>> => {
    if (busy.current) return BUSY
    busy.current = true
    setPending(true)
    setError(undefined)
    try {
      const value = await actionRef.current(...args)
      return { ok: true, value }
    } catch (err) {
      setError(err)
      return { ok: false, error: err }
    } finally {
      busy.current = false
      setPending(false)
    }
  }, [])

  const clearError = useCallback(() => setError(undefined), [])
  return { run, pending, error, clearError }
}
