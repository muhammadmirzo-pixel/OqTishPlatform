import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { I18nContext, interpolate, type I18nValue } from './context'
import { catalog, locales, type Locale } from './messages'

const STORAGE_KEY = 'oqtish.locale'

function initialLocale(): Locale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored && (locales as readonly string[]).includes(stored)) return stored as Locale
  } catch {
    // Storage can be unavailable (private mode, Telegram webviews); fall back to Uzbek.
  }
  return 'uz'
}

export function I18nProvider({ children, initial }: { children: ReactNode; initial?: Locale }) {
  const [locale, setLocaleState] = useState<Locale>(initial ?? initialLocale)

  useEffect(() => {
    document.documentElement.lang = locale
  }, [locale])

  const setLocale = useCallback((next: Locale) => {
    setLocaleState(next)
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // Non-critical convenience setting.
    }
  }, [])

  const value = useMemo<I18nValue>(
    () => ({ locale, setLocale, t: (key, params) => interpolate(catalog[locale][key], params) }),
    [locale, setLocale],
  )

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}
