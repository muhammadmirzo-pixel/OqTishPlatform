import { describe, expect, it } from 'vitest'
import { formatLocalPhone, formatPhone, isValidUzPhone, localPhoneDigits, normalizePhone } from './phone'

describe('phone', () => {
  it('accepts only known operator codes', () => {
    for (const code of ['20', '33', '50', '55', '71', '75', '77', '88', '90', '91', '92', '93', '94', '95', '97', '98', '99']) {
      expect(isValidUzPhone(`+998${code}1234567`)).toBe(true)
    }
    for (const code of ['10', '45', '60', '89', '96']) {
      expect(isValidUzPhone(`+998${code}1234567`)).toBe(false)
    }
    expect(isValidUzPhone('90 123 45 67')).toBe(true)
    expect(isValidUzPhone('+998 90 123 45 6')).toBe(false)
  })

  it('extracts local digits from any form', () => {
    expect(localPhoneDigits('+998901234567')).toBe('901234567')
    expect(localPhoneDigits('998901234567')).toBe('901234567')
    expect(localPhoneDigits('00998901234567')).toBe('901234567')
    expect(localPhoneDigits('99 812 34 56')).toBe('998123456')
    expect(localPhoneDigits('+998')).toBe('')
    expect(localPhoneDigits('90123456789')).toBe('901234567')
    expect(normalizePhone(`+998${localPhoneDigits('90 123 45 67')}`)).toBe('+998901234567')
  })

  it('formats typed, full and masked numbers', () => {
    expect(formatLocalPhone('9012')).toBe('90 12')
    expect(formatLocalPhone('901234567')).toBe('90 123 45 67')
    expect(formatPhone('+998901234567')).toBe('+998 90 123 45 67')
    expect(formatPhone('+998*****4567')).toBe('+998 ** *** 45 67')
    expect(formatPhone(null)).toBe('')
  })
})
