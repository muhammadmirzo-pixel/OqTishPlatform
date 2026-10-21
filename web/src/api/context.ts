import { createContext, useContext } from 'react'
import type { Api } from './index'

export const ApiContext = createContext<Api | null>(null)

/** URL of an uploaded image (logo, avatar) or null when there is none. */
export function useImageUrl(id: string | null | undefined): string | null {
  const api = useContext(ApiContext)
  return id && api ? api.client.imageUrl(id) : null
}

export function useApi(): Api {
  const api = useContext(ApiContext)
  if (!api) throw new Error('useApi must be used inside <ApiContext.Provider>')
  return api
}
