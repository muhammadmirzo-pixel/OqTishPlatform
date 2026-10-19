import type { Locale } from '../i18n/messages'
import type { Money } from '../api/types'

const intlLocale: Record<Locale, string> = { uz: 'uz-Latn-UZ', 'uz-Cyrl': 'uz-Cyrl-UZ', ru: 'ru-RU', en: 'en-GB' }
const currencyLabel: Record<Locale, string> = { uz: "so'm", 'uz-Cyrl': 'сўм', ru: 'сум', en: 'UZS' }

/**
 * Display-only money formatting. The decimal string from the API is split, not converted to a
 * float, so large or fractional amounts never lose precision on the way to the screen.
 */
export function formatMoney(money: Money, locale: Locale): string {
  const [whole = '0', fraction = ''] = money.amount.split('.')
  const negative = whole.startsWith('-')
  const digits = negative ? whole.slice(1) : whole
  const group = locale === 'en' ? ',' : ' '
  const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, group)
  const decimals = fraction && /[1-9]/.test(fraction) ? (locale === 'en' ? '.' : ',') + fraction.padEnd(2, '0').slice(0, 2) : ''
  return `${negative ? '−' : ''}${grouped}${decimals} ${currencyLabel[locale]}`
}

/** Formats a UTC instant in the branch's time zone (stored UTC, displayed branch-local). */
export function formatDateTime(isoUtc: string, timeZone: string, locale: Locale): string {
  return new Intl.DateTimeFormat(intlLocale[locale], {
    timeZone,
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).format(new Date(isoUtc))
}

/** Date only ("18 Oct 2026") of a UTC instant in the given zone. */
export function formatInstantDate(isoUtc: string, timeZone: string, locale: Locale): string {
  return new Intl.DateTimeFormat(intlLocale[locale], { timeZone, day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(isoUtc))
}

/** Full date and time with year, for audit trails and history. */
export function formatFullDateTime(isoUtc: string, timeZone: string, locale: Locale): string {
  return new Intl.DateTimeFormat(intlLocale[locale], {
    timeZone,
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).format(new Date(isoUtc))
}

/** Weekday + date of a local "YYYY-MM-DD" (for date pickers). */
export function formatLocalDay(isoDate: string, locale: Locale): { weekday: string; day: string } {
  const d = new Date(`${isoDate}T12:00:00Z`)
  return {
    weekday: new Intl.DateTimeFormat(intlLocale[locale], { weekday: 'short', timeZone: 'UTC' }).format(d),
    day: new Intl.DateTimeFormat(intlLocale[locale], { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(d),
  }
}

export function formatTime(isoUtc: string, timeZone: string, locale: Locale): string {
  return new Intl.DateTimeFormat(intlLocale[locale], { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(isoUtc))
}

export function formatDate(isoDate: string, locale: Locale): string {
  return new Intl.DateTimeFormat(intlLocale[locale], { day: '2-digit', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${isoDate}T00:00:00Z`))
}

/** "mm:ss" until a deadline; null once passed. Display only — the server enforces the deadline. */
export function remaining(deadlineIso: string, nowMs: number): string | null {
  const ms = new Date(deadlineIso).getTime() - nowMs
  if (ms <= 0) return null
  const totalSeconds = Math.floor(ms / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}
