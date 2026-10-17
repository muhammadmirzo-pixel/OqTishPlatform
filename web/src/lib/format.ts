import type { Locale } from '../i18n/messages'
import type { Money } from '../domain/types'

const intlLocale: Record<Locale, string> = { uz: 'uz-Latn-UZ', ru: 'ru-RU', en: 'en-GB' }
const currencyLabel: Record<Locale, string> = { uz: "so'm", ru: 'сум', en: 'UZS' }

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
