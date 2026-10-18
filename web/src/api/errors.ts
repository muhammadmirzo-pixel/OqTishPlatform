// Maps API error codes (docs/API.md §9) to localised messages. Screens call `errorMessage(t, err)`
// and never show raw server text, so every language gets a proper sentence.

import type { MessageKey } from '../i18n/messages'
import { ApiError } from './client'

export const errorCodes = [
  'common.validation',
  'common.not_found',
  'common.forbidden',
  'common.idempotency_mismatch',
  'common.concurrency',
  'security.client_header_missing',
  'auth.unauthenticated',
  'auth.invalid_credentials',
  'auth.totp_required',
  'auth.totp_invalid',
  'auth.totp_setup_required',
  'auth.otp_invalid',
  'auth.otp_expired',
  'auth.otp_attempts_exceeded',
  'auth.rate_limited',
  'auth.identity_in_use',
  'auth.account_suspended',
  'auth.telegram_invalid',
  'auth.totp_already_enabled',
  'auth.totp_not_enabled',
  'auth.current_password_invalid',
  'auth.phone_verification_required',
  'auth.last_identity',
  'account.delete_blocked',
  'telegram.disabled',
  'telegram.link_invalid',
  'common.method_not_allowed',
  'common.internal',
  'booking.slot_unavailable',
  'booking.outside_hours',
  'booking.lead_time',
  'booking.too_many_pending',
  'booking.clinic_unavailable',
  'booking.not_pending',
  'booking.expired',
  'booking.doctor_not_eligible',
  'booking.doctor_busy',
  'booking.invalid_transition',
  'booking.cancel_too_late',
  'booking.too_early_for_outcome',
  'schedule.timeoff_conflict',
  'staff.cannot_grant_role',
  'staff.last_owner',
  'finance.refund_exceeds_paid',
  'finance.amount_invalid',
  'finance.period_locked',
  'finance.settlement_exists',
  'finance.rule_overlap',
  'review.not_eligible',
  'review.already_exists',
] as const

export type ErrorCode = (typeof errorCodes)[number]

export function messageKeyForCode(code: string): MessageKey | null {
  return (errorCodes as readonly string[]).includes(code) ? (`err.${code}` as MessageKey) : null
}

type Translate = (key: MessageKey, params?: Record<string, string | number>) => string

/** A localised, user-facing sentence for any thrown value. */
export function errorMessage(t: Translate, error: unknown): string {
  if (!(error instanceof ApiError)) return t('err.server')
  const byCode = error.code ? messageKeyForCode(error.code) : null
  if (byCode) return t(byCode)
  switch (error.kind) {
    case 'network':
      return t('err.network')
    case 'server':
      return t('err.server')
    default:
      return t(`err.kind.${error.kind}`)
  }
}
