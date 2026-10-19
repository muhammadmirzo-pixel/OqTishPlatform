import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import { useIdempotencyKeeper } from '../../api/idempotency'
import { paymentMethods, type Money, type PaymentDto, type PaymentMethod, type RecordPaymentBody } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { amountForInput, compareAmounts, isPositive, parseAmount, uzs } from '../../lib/money'
import { Field, FormError, Notice, SubmitButton } from '../../ui/forms'
import { Select } from '../../ui/Select'
import { validatePayment } from './paymentValidation'

/**
 * Records a payment for one appointment. The amount is validated as a decimal string (never a
 * float). The Idempotency-Key stays the same while the user retries the same entry, so a double
 * click or a retry after a timeout cannot record the payment twice.
 */
export function RecordPaymentForm({ clinicId, appointmentId, outstanding, onRecorded }: { clinicId: string; appointmentId: string; outstanding?: Money; onRecorded: (payment: PaymentDto) => void }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const keeper = useIdempotencyKeeper()
  const [amount, setAmount] = useState(outstanding && isPositive(outstanding.amount) ? amountForInput(outstanding.amount) : '')
  const [method, setMethod] = useState('')
  const [externalRef, setExternalRef] = useState('')
  const [touched, setTouched] = useState(false)
  const [recorded, setRecorded] = useState<PaymentDto | null>(null)
  const record = useAction((body: RecordPaymentBody) => api.finance.recordPayment(clinicId, appointmentId, body, keeper.keyFor(body)))
  const errors = validatePayment(amount, method, externalRef, t)
  const parsed = parseAmount(amount)
  const exceeds = parsed && outstanding && compareAmounts(parsed, outstanding.amount) > 0

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    setRecorded(null)
    if (errors.amount || errors.method || errors.externalRef || !parsed) return
    const body: RecordPaymentBody = { amount: uzs(parsed), method: method as PaymentMethod, ...(externalRef.trim() ? { externalRef: externalRef.trim() } : {}) }
    const r = await record.run(body)
    if (r.ok) {
      keeper.reset()
      setRecorded(r.value)
      setAmount('')
      setExternalRef('')
      setTouched(false)
      onRecorded(r.value)
    } else {
      keeper.afterError(r.error)
    }
  }

  return (
    <form className="stack" onSubmit={submit} noValidate aria-labelledby="record-payment-title">
      <h3 id="record-payment-title">{t('pay.record')}</h3>
      <div className="grid-2">
        <Field label={t('pay.amount')} hint={outstanding ? t('pay.outstandingHint', { amount: formatMoney(outstanding, locale) }) : undefined} error={touched ? errors.amount : null}>
          {(p) => <input {...p} inputMode="decimal" autoComplete="off" value={amount} onChange={(e) => setAmount(e.target.value)} />}
        </Field>
        <Field label={t('pay.method')} error={touched ? errors.method : null}>
          {(p) => (
            <Select {...p} value={method} onChange={setMethod} options={[{ value: '', label: t('pay.chooseMethod') }, ...paymentMethods.map((m) => ({ value: m, label: t(`pay.${m}`) }))]} />
          )}
        </Field>
      </div>
      {(method === 'Card' || method === 'Transfer') && (
        <Field label={t('pay.externalRef')} hint={t('pay.externalRefHint')} error={touched ? errors.externalRef : null} optional>
          {(p) => <input {...p} maxLength={100} value={externalRef} onChange={(e) => setExternalRef(e.target.value)} />}
        </Field>
      )}
      {exceeds && <p className="alert alert--warning">{t('pay.exceedsOutstanding')}</p>}
      <FormError error={record.error} />
      {recorded && <Notice>{t('pay.recorded', { amount: formatMoney(recorded.amount, locale) })}</Notice>}
      <SubmitButton pending={record.pending}>{t('pay.submit')}</SubmitButton>
    </form>
  )
}
