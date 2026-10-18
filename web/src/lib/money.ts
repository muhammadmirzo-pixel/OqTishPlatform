// Decimal-string helpers for money input. Amounts never pass through floating point: values are
// validated as strings and compared as integer tiyin via BigInt. The server re-validates everything.

import type { Money } from '../api/types'

/**
 * Normalises user input ("150 000", "150000,5", "1500000.00") to "150000.50"-style strings with
 * no grouping and "." as separator. Returns null for anything that is not a non-negative amount
 * with at most two decimals.
 */
export function parseAmount(input: string): string | null {
  const compact = input.replace(/[\s  ']/g, '').replace(',', '.')
  if (!/^\d+(\.\d{1,2})?$/.test(compact)) return null
  const [whole = '0', fraction = ''] = compact.split('.')
  const trimmed = whole.replace(/^0+(?=\d)/, '')
  return `${trimmed}.${fraction.padEnd(2, '0')}`
}

export function toTiyin(amount: string): bigint {
  const negative = amount.startsWith('-')
  const [whole = '0', fraction = ''] = (negative ? amount.slice(1) : amount).split('.')
  const value = BigInt(whole || '0') * 100n + BigInt((fraction + '00').slice(0, 2))
  return negative ? -value : value
}

export function fromTiyin(value: bigint): string {
  const negative = value < 0n
  const abs = negative ? -value : value
  const whole = abs / 100n
  const fraction = (abs % 100n).toString().padStart(2, '0')
  return `${negative ? '-' : ''}${whole}.${fraction}`
}

export function uzs(amount: string): Money {
  return { amount, currency: 'UZS' }
}

export function isPositive(amount: string): boolean {
  return toTiyin(amount) > 0n
}

export function compareAmounts(a: string, b: string): number {
  const d = toTiyin(a) - toTiyin(b)
  return d === 0n ? 0 : d > 0n ? 1 : -1
}

export function sumMoney(values: Money[]): Money {
  return uzs(fromTiyin(values.reduce((acc, m) => acc + toTiyin(m.amount), 0n)))
}

/** Plain input value for an amount from the API ("150000.00" → "150000", "99.50" → "99.50"). */
export function amountForInput(amount: string): string {
  return amount.endsWith('.00') ? amount.slice(0, -3) : amount
}

/** "7.5" (percent) → "0.0750" (fraction, 4 dp, matching numeric(7,4)). Null when invalid or > 100. */
export function percentToFraction(input: string): string | null {
  const compact = input.trim().replace(',', '.')
  if (!/^\d{1,3}(\.\d{1,2})?$/.test(compact)) return null
  const [whole = '0', fraction = ''] = compact.split('.')
  const hundredths = BigInt(whole) * 100n + BigInt((fraction + '00').slice(0, 2))
  if (hundredths <= 0n || hundredths > 10000n) return null
  const value = hundredths.toString().padStart(5, '0')
  return `${value.slice(0, -4) || '0'}.${value.slice(-4)}`
}

/** "0.0750" → "7.5" for display. */
export function fractionToPercent(fraction: string): string {
  const [whole = '0', frac = ''] = fraction.split('.')
  const digits = (whole + (frac + '0000').slice(0, 4)).replace(/^0+(?=\d)/, '')
  const padded = digits.padStart(3, '0')
  const intPart = padded.slice(0, -2).replace(/^0+(?=\d)/, '')
  const decPart = padded.slice(-2).replace(/0+$/, '')
  return decPart ? `${intPart}.${decPart}` : intPart
}
