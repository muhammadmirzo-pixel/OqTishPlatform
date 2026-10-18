import { paymentMethods, type PaymentMethod } from '../../api/types'
import type { MessageKey } from '../../i18n/messages'
import { isPositive, parseAmount } from '../../lib/money'

export interface PaymentFormErrors {
  amount: string | null
  method: string | null
  externalRef: string | null
}

export function validatePayment(amount: string, method: string, externalRef: string, t: (key: MessageKey, params?: Record<string, string | number>) => string): PaymentFormErrors {
  const parsed = parseAmount(amount)
  return {
    amount: !amount.trim() ? t('valid.required') : parsed === null ? t('valid.amount') : !isPositive(parsed) ? t('valid.amountPositive') : null,
    method: !paymentMethods.includes(method as PaymentMethod) ? t('pay.chooseMethod') : null,
    externalRef: externalRef.length > 100 ? t('valid.maxLength', { max: 100 }) : null,
  }
}
