import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { I18nContext, interpolate, type I18nValue } from './context'
import { catalog, isLocale, type Locale } from './messages'

const STORAGE_KEY = 'oqtish.locale'

function storedLocale(): Locale | null {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    return stored && isLocale(stored) ? stored : null
  } catch {
    // Storage can be unavailable (private mode, Telegram webviews).
    return null
  }
}

export function I18nProvider({ children, initial }: { children: ReactNode; initial?: Locale }) {
  const [locale, setLocaleState] = useState<Locale>(() => initial ?? storedLocale() ?? 'uz')

  useEffect(() => {
    document.documentElement.lang = locale
  }, [locale])

  const setLocale = useCallback((next: Locale, options?: { persist?: boolean }) => {
    setLocaleState(next)
    if (options?.persist === false) return
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // Non-critical convenience setting.
    }
  }, [])

  const value = useMemo<I18nValue>(
    () => ({
      locale,
      setLocale,
      hasUserChoice: () => storedLocale() !== null,
      t: (key, params) => interpolate(catalog[locale][key] ?? key, params),
    }),
    [locale, setLocale],
  )

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}
