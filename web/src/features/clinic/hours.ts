import { weekdays, type Weekday } from '../../api/types'
import { isValidTime, toMinutes } from '../../lib/time'

export interface Interval {
  weekday: Weekday
  start: string
  end: string
}

export function intervalError(i: Interval): 'time' | 'range' | null {
  if (!isValidTime(i.start) || !isValidTime(i.end)) return 'time'
  return toMinutes(i.start) < toMinutes(i.end) ? null : 'range'
}

/** True when any two intervals of the same weekday overlap. */
export function hasOverlap(list: Interval[]): boolean {
  return weekdays.some((d) => {
    const day = list.filter((i) => i.weekday === d).sort((a, b) => toMinutes(a.start) - toMinutes(b.start))
    return day.some((i, n) => n > 0 && toMinutes(i.start) < toMinutes(day[n - 1]!.end))
  })
}
