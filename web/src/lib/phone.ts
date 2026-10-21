// Uzbek numbers: +998 followed by a 2-digit operator/area code and 7 digits. Input is normalised before
// validation so users may type spaces, dashes, brackets, a leading 998 or only the 9 local digits.

/** Operator and area codes accepted after +998 (mirrors PhoneNumber.cs on the API). */
export const UZ_PHONE_CODES = ['20', '33', '50', '55', '71', '75', '77', '88', '90', '91', '92', '93', '94', '95', '97', '98', '99'] as const

const validPhone = new RegExp(`^\\+998(${UZ_PHONE_CODES.join('|')})\\d{7}$`)

export function normalizePhone(input: string): string {
  const digits = input.replace(/[^\d+]/g, '')
  if (/^\+998\d{9}$/.test(digits)) return digits
  const bare = digits.replace(/^\+/, '').replace(/^00(?=998)/, '')
  if (/^998\d{9}$/.test(bare)) return `+${bare}`
  if (/^\d{9}$/.test(bare)) return `+998${bare}`
  return digits
}

export function isValidUzPhone(input: string): boolean {
  return validPhone.test(normalizePhone(input))
}

/**
 * Up to 9 national digits from any form: "+998 90 123…", "998901234567", "00998…" or bare local digits.
 * A leading 998 is only stripped when it is clearly the country code (the value starts with "+" or is
 * longer than a national number), so a local number such as "99 812 34 56" stays intact.
 */
export function localPhoneDigits(input: string): string {
  const trimmed = input.trim()
  let digits = trimmed.replace(/\D/g, '')
  if (trimmed.startsWith('+998') || (digits.length > 9 && digits.startsWith('998'))) digits = digits.slice(3)
  else if (digits.startsWith('00998')) digits = digits.slice(5)
  return digits.slice(0, 9)
}

/** "901234567" (or a prefix of it) → "90 123 45 67", as the user types. */
export function formatLocalPhone(digits: string): string {
  return [digits.slice(0, 2), digits.slice(2, 5), digits.slice(5, 7), digits.slice(7, 9)].filter(Boolean).join(' ')
}

/**
 * "+998901234567" → "+998 90 123 45 67"; the masked "+998*****4567" → "+998 ** *** 45 67".
 * Anything else is returned unchanged.
 */
export function formatPhone(phone: string | null | undefined): string {
  if (!phone) return ''
  const m = /^\+998([\d*]{2})([\d*]{3})([\d*]{2})([\d*]{2})$/.exec(phone.replace(/\s/g, ''))
  return m ? `+998 ${m[1]} ${m[2]} ${m[3]} ${m[4]}` : phone
}
