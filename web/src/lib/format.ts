import type { Locale } from '../i18n/messages'
import type { Money } from '../api/types'

const intlLocale: Record<Locale, string> = { uz: 'uz-Latn-UZ', 'uz-Cyrl': 'uz-Cyrl-UZ', ru: 'ru-RU', en: 'en-GB' }
const currencyLabel: Record<Locale, string> = { uz: 'so‘m', 'uz-Cyrl': 'сўм', ru: 'сум', en: 'UZS' }

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

// Browsers ship no Uzbek month/weekday names (Intl prints "M10"), so Uzbek dates are composed
// from numeric parts and the tables below; other locales use Intl directly.
const uzMonthsShort = ['yan', 'fev', 'mar', 'apr', 'may', 'iyun', 'iyul', 'avg', 'sen', 'okt', 'noy', 'dek']
const uzMonthsLong = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr']
const uzWeekdaysShort = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh', 'Ya']
const cyMonthsShort = ['янв', 'фев', 'мар', 'апр', 'май', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек']
const cyMonthsLong = ['январь', 'февраль', 'март', 'апрель', 'май', 'июнь', 'июль', 'август', 'сентябрь', 'октябрь', 'ноябрь', 'декабрь']
const cyWeekdaysShort = ['Ду', 'Се', 'Чо', 'Па', 'Жу', 'Ша', 'Як']
const weekdayIndex: Record<string, number> = { Mon: 0, Tue: 1, Wed: 2, Thu: 3, Fri: 4, Sat: 5, Sun: 6 }

interface DateParts {
  year: string
  month: number // 0-based
  day: string
  hour: string
  minute: string
  weekday: number // 0 = Monday
}

function partsOf(date: Date, timeZone: string): DateParts {
  const map: Record<string, string> = {}
  for (const p of new Intl.DateTimeFormat('en-GB', {
    timeZone,
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    weekday: 'short',
  }).formatToParts(date)) {
    map[p.type] = p.value
  }
  return {
    year: map.year ?? '',
    month: Number(map.month ?? 1) - 1,
    day: String(Number(map.day ?? 1)),
    hour: map.hour ?? '00',
    minute: map.minute ?? '00',
    weekday: weekdayIndex[map.weekday ?? 'Mon'] ?? 0,
  }
}

function uzNames(locale: Locale) {
  return locale === 'uz-Cyrl'
    ? { short: cyMonthsShort, long: cyMonthsLong, weekdays: cyWeekdaysShort }
    : { short: uzMonthsShort, long: uzMonthsLong, weekdays: uzWeekdaysShort }
}

const isUz = (locale: Locale) => locale === 'uz' || locale === 'uz-Cyrl'

/** Formats a UTC instant in the branch's time zone (stored UTC, displayed branch-local). */
export function formatDateTime(isoUtc: string, timeZone: string, locale: Locale): string {
  if (isUz(locale)) {
    const p = partsOf(new Date(isoUtc), timeZone)
    return `${p.day}-${uzNames(locale).short[p.month]}, ${p.hour}:${p.minute}`
  }
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
  if (isUz(locale)) {
    const p = partsOf(new Date(isoUtc), timeZone)
    return `${p.day}-${uzNames(locale).short[p.month]} ${p.year}`
  }
  return new Intl.DateTimeFormat(intlLocale[locale], { timeZone, day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(isoUtc))
}

/** Full date and time with year, for audit trails and history. */
export function formatFullDateTime(isoUtc: string, timeZone: string, locale: Locale): string {
  if (isUz(locale)) {
    const p = partsOf(new Date(isoUtc), timeZone)
    return `${p.day}-${uzNames(locale).short[p.month]} ${p.year}, ${p.hour}:${p.minute}`
  }
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
  if (isUz(locale)) {
    const p = partsOf(d, 'UTC')
    const names = uzNames(locale)
    return { weekday: names.weekdays[p.weekday] ?? '', day: `${p.day}-${names.short[p.month]}` }
  }
  return {
    weekday: new Intl.DateTimeFormat(intlLocale[locale], { weekday: 'short', timeZone: 'UTC' }).format(d),
    day: new Intl.DateTimeFormat(intlLocale[locale], { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(d),
  }
}

export function formatTime(isoUtc: string, timeZone: string, locale: Locale): string {
  return new Intl.DateTimeFormat(intlLocale[locale], { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(isoUtc))
}

export function formatDate(isoDate: string, locale: Locale): string {
  if (isUz(locale)) {
    const p = partsOf(new Date(`${isoDate}T00:00:00Z`), 'UTC')
    return `${p.day}-${uzNames(locale).long[p.month]} ${p.year}`
  }
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
