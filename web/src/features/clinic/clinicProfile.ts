import type { RegisterClinicBody } from '../../api/types'
import type { MessageKey } from '../../i18n/messages'

export type ClinicProfileErrors = Partial<Record<keyof RegisterClinicBody, string | null>>

export function validateClinicProfile(v: RegisterClinicBody, t: (k: MessageKey, p?: Record<string, string | number>) => string, validPhone: (p: string) => boolean): ClinicProfileErrors {
  // Limits of POST/PUT /clinics (API.md §4.1): only the name is required.
  const max = (s: string, n: number) => (s.trim().length > n ? t('valid.maxLength', { max: n }) : null)
  return {
    name: !v.name.trim() ? t('valid.required') : max(v.name, 200),
    legalName: max(v.legalName, 300),
    description: max(v.description, 4000),
    phone: v.phone.trim() && !validPhone(v.phone) ? t('valid.phone') : null,
  }
}
