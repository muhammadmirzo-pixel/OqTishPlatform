import { useCallback } from 'react'
import { useSearchParams } from 'react-router'

/** The console's branch filter lives in the URL (?branch=<id>) so views are linkable and survive reloads. */
export function useBranchParam(): [string | undefined, (branchId: string | undefined) => void] {
  const [params, setParams] = useSearchParams()
  const branch = params.get('branch') ?? undefined
  const setBranch = useCallback(
    (next: string | undefined) => {
      setParams(
        (prev) => {
          const copy = new URLSearchParams(prev)
          if (next) copy.set('branch', next)
          else copy.delete('branch')
          copy.delete('page')
          return copy
        },
        { replace: true },
      )
    },
    [setParams],
  )
  return [branch, setBranch]
}
