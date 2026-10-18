import { createContext, useContext } from 'react'
import type { Locale, MessageKey } from './messages'

export type Params = Record<string, string | number>

export interface I18nValue {
  locale: Locale
  /** `persist: false` applies a locale without recording it as the user's own choice. */
  setLocale: (locale: Locale, options?: { persist?: boolean }) => void
  /** True when the user picked a language explicitly (stored preference). */
  hasUserChoice: () => boolean
  t: (key: MessageKey, params?: Params) => string
}

export const I18nContext = createContext<I18nValue | null>(null)

export function interpolate(template: string, params?: Params): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match))
}

export function useI18n(): I18nValue {
  const value = useContext(I18nContext)
  if (!value) throw new Error('useI18n must be used inside <I18nProvider>')
  return value
}
