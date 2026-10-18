// Uzbek mobile numbers: +998 followed by 9 digits. Input is normalised before validation so users
// may type spaces, dashes, brackets, a leading 998 or only the 9 local digits.

export function normalizePhone(input: string): string {
  const digits = input.replace(/[^\d+]/g, '')
  if (/^\+998\d{9}$/.test(digits)) return digits
  const bare = digits.replace(/^\+/, '').replace(/^00(?=998)/, '')
  if (/^998\d{9}$/.test(bare)) return `+${bare}`
  if (/^\d{9}$/.test(bare)) return `+998${bare}`
  return digits
}

export function isValidUzPhone(input: string): boolean {
  return /^\+998\d{9}$/.test(normalizePhone(input))
}

/** "+998901234567" → "+998 90 123 45 67" for display of numbers the user typed themselves. */
export function formatPhone(phone: string): string {
  const m = /^\+998(\d{2})(\d{3})(\d{2})(\d{2})$/.exec(phone)
  return m ? `+998 ${m[1]} ${m[2]} ${m[3]} ${m[4]}` : phone
}
