import { Suspense, lazy, type ComponentType } from 'react'
import { LoadingState } from '../ui/states'

/** Route element factory for a lazily loaded chunk: from(() => import('./chunk'))('PageName'). */
type Chunk = Record<string, unknown>
export function from<M extends Chunk>(load: () => Promise<M>) {
  return (name: keyof M & string) => {
    const C = lazy(async () => ({ default: (await load())[name] as ComponentType }))
    return (
      <Suspense fallback={<LoadingState />}>
        <C />
      </Suspense>
    )
  }
}
