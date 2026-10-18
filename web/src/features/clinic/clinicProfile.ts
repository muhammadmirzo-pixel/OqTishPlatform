import type { RegisterClinicBody } from '../../api/types'
import type { MessageKey } from '../../i18n/messages'

export type ClinicProfileErrors = Partial<Record<keyof RegisterClinicBody, string | null>>

export function validateClinicProfile(v: RegisterClinicBody, t: (k: MessageKey, p?: Record<string, string | number>) => string, validPhone: (p: string) => boolean): ClinicProfileErrors {
  const req = (s: string, max: number) => (!s.trim() ? t('valid.required') : s.trim().length > max ? t('valid.maxLength', { max }) : null)
  return {
    name: req(v.name, 120),
    legalName: req(v.legalName, 200),
    description: v.description.length > 2000 ? t('valid.maxLength', { max: 2000 }) : null,
    phone: !v.phone.trim() ? t('valid.required') : !validPhone(v.phone) ? t('valid.phone') : null,
  }
}
