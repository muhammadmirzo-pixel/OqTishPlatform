// Time-zone helpers built on Intl only (no date library). Instants travel as UTC ISO strings;
// business-local values (dates, wall-clock times) are interpreted in the branch's IANA time zone.

import type { Weekday } from '../api/types'

export const DEFAULT_TIME_ZONE = 'Asia/Tashkent'

interface ZonedParts {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second: number
}

const formatters = new Map<string, Intl.DateTimeFormat>()

function formatterFor(timeZone: string): Intl.DateTimeFormat {
  let f = formatters.get(timeZone)
  if (!f) {
    f = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
    formatters.set(timeZone, f)
  }
  return f
}

export function zonedParts(instant: Date | string | number, timeZone: string): ZonedParts {
  const date = instant instanceof Date ? instant : new Date(instant)
  const parts: Record<string, number> = {}
  for (const p of formatterFor(timeZone).formatToParts(date)) {
    if (p.type !== 'literal') parts[p.type] = Number(p.value)
  }
  return {
    year: parts.year ?? 1970,
    month: parts.month ?? 1,
    day: parts.day ?? 1,
    hour: (parts.hour ?? 0) % 24,
    minute: parts.minute ?? 0,
    second: parts.second ?? 0,
  }
}

const pad = (n: number) => String(n).padStart(2, '0')

/** "YYYY-MM-DD" of the instant in the given zone. */
export function localDate(instant: Date | string | number, timeZone: string): string {
  const p = zonedParts(instant, timeZone)
  return `${p.year}-${pad(p.month)}-${pad(p.day)}`
}

/** "HH:mm" of the instant in the given zone. */
export function localTime(instant: Date | string | number, timeZone: string): string {
  const p = zonedParts(instant, timeZone)
  return `${pad(p.hour)}:${pad(p.minute)}`
}

/** Minutes since local midnight of the instant in the given zone. */
export function localMinutes(instant: Date | string | number, timeZone: string): number {
  const p = zonedParts(instant, timeZone)
  return p.hour * 60 + p.minute
}

function offsetMs(utcMs: number, timeZone: string): number {
  const p = zonedParts(utcMs, timeZone)
  return Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second) - Math.floor(utcMs / 1000) * 1000
}

/** Converts a branch-local wall-clock date + "HH:mm" to a UTC ISO instant (DST-safe). */
export function zonedToUtc(date: string, time: string, timeZone: string): string {
  const [y, m, d] = date.split('-').map(Number)
  const [hh, mm] = time.split(':').map(Number)
  const wall = Date.UTC(y ?? 1970, (m ?? 1) - 1, d ?? 1, hh ?? 0, mm ?? 0)
  let utc = wall - offsetMs(wall, timeZone)
  const second = wall - offsetMs(utc, timeZone)
  if (second !== utc) utc = second
  return new Date(utc).toISOString().replace('.000Z', 'Z')
}

export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number)
  const t = new Date(Date.UTC(y ?? 1970, (m ?? 1) - 1, (d ?? 1) + days))
  return t.toISOString().slice(0, 10)
}

/** ISO weekday (1 = Monday … 7 = Sunday) of a local date. */
export function weekdayOf(date: string): Weekday {
  const [y, m, d] = date.split('-').map(Number)
  const js = new Date(Date.UTC(y ?? 1970, (m ?? 1) - 1, d ?? 1)).getUTCDay()
  return (js === 0 ? 7 : js) as Weekday
}

export function toMinutes(hhmm: string): number {
  const [h, m] = hhmm.split(':').map(Number)
  return (h ?? 0) * 60 + (m ?? 0)
}

export function fromMinutes(total: number): string {
  return `${pad(Math.floor(total / 60))}:${pad(total % 60)}`
}

export function isValidDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`))
}

export function isValidTime(value: string): boolean {
  return /^([01]\d|2[0-3]):[0-5]\d$/.test(value)
}

/** Splits an <input type="datetime-local"> value into date and time. */
export function splitLocalDateTime(value: string): { date: string; time: string } | null {
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(value)
  return match ? { date: match[1] ?? '', time: match[2] ?? '' } : null
}

/**
 * Inclusive local date range → [from, to) UTC instants for list filters
 * (from = start of the first day, to = start of the day after the last day, branch-local).
 */
export function localRangeToUtc(from: string | undefined, to: string | undefined, timeZone: string): { from?: string; to?: string } {
  return {
    from: from && isValidDate(from) ? zonedToUtc(from, '00:00', timeZone) : undefined,
    to: to && isValidDate(to) ? zonedToUtc(addDays(to, 1), '00:00', timeZone) : undefined,
  }
}
