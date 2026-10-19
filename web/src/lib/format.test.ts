import { describe, expect, it } from 'vitest'
import { formatMoney, formatTime, remaining } from './format'

describe('formatMoney', () => {
  it('groups whole sums per locale without float conversion', () => {
    expect(formatMoney({ amount: '1500000', currency: 'UZS' }, 'uz')).toBe("1 500 000 so‘m")
    expect(formatMoney({ amount: '1500000.00', currency: 'UZS' }, 'ru')).toBe('1 500 000 сум')
    expect(formatMoney({ amount: '1500000', currency: 'UZS' }, 'en')).toBe('1,500,000 UZS')
  })

  it('keeps precision for amounts beyond float safety and shows real fractions', () => {
    expect(formatMoney({ amount: '9007199254740993.50', currency: 'UZS' }, 'en')).toBe('9,007,199,254,740,993.50 UZS')
    expect(formatMoney({ amount: '-25000.5', currency: 'UZS' }, 'uz')).toBe("−25 000,50 so‘m")
  })
})

describe('formatTime', () => {
  it('shows UTC instants in the branch time zone', () => {
    // 05:00 UTC = 10:00 in Tashkent (UTC+5).
    expect(formatTime('2026-10-20T05:00:00Z', 'Asia/Tashkent', 'en')).toBe('10:00')
  })
})

describe('remaining', () => {
  const deadline = '2026-10-20T05:30:00Z'
  const at = (iso: string) => new Date(iso).getTime()

  it('counts down in mm:ss', () => {
    expect(remaining(deadline, at('2026-10-20T05:00:00Z'))).toBe('30:00')
    expect(remaining(deadline, at('2026-10-20T05:29:59Z'))).toBe('00:01')
  })

  it('returns null at and after the deadline', () => {
    expect(remaining(deadline, at('2026-10-20T05:30:00Z'))).toBeNull()
    expect(remaining(deadline, at('2026-10-20T06:00:00Z'))).toBeNull()
  })
})

describe('Uzbek dates', () => {
  it('uses Uzbek month names instead of the browser fallback', async () => {
    const { formatDateTime, formatDate, formatLocalDay } = await import('./format')
    expect(formatDateTime('2026-10-19T04:30:00Z', 'Asia/Tashkent', 'uz')).toBe('19-okt, 09:30')
    expect(formatDateTime('2026-10-19T04:30:00Z', 'Asia/Tashkent', 'uz-Cyrl')).toBe('19-окт, 09:30')
    expect(formatDate('2026-03-08', 'uz')).toBe('8-mart 2026')
    expect(formatLocalDay('2026-10-19', 'uz')).toEqual({ weekday: 'Du', day: '19-okt' })
  })
})
