import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import { useIdempotencyKeeper } from '../../api/idempotency'
import type { PaymentDto, RefundBody } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { amountForInput, compareAmounts, isPositive, parseAmount, uzs } from '../../lib/money'
import { Dialog } from '../../ui/Dialog'
import { Field, FormError, SubmitButton } from '../../ui/forms'
import { ReasonDialog } from '../../ui/ReasonDialog'
import { useClinic } from '../clinic/clinicContext'
import { Perm } from '../clinic/permissions'

/** Refund / mark-failed buttons for a recorded incoming payment (permissions from the membership). */
export function PaymentActions({ payment, onChanged }: { payment: PaymentDto; onChanged: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId, can } = useClinic()
  const [dialog, setDialog] = useState<'refund' | 'fail' | null>(null)
  if (payment.direction !== 'In' || payment.status !== 'Recorded') return null

  return (
    <div className="row">
      {can(Perm.paymentRefund) && (
        <button type="button" className="btn btn--ghost btn--small" onClick={() => setDialog('refund')}>
          {t('pay.refund')}
        </button>
      )}
      {can(Perm.paymentRecord) && payment.method === 'Card' && (
        <button type="button" className="btn btn--ghost btn--small" onClick={() => setDialog('fail')}>
          {t('pay.markFailed')}
        </button>
      )}
      {dialog === 'refund' && (
        <RefundDialog
          payment={payment}
          onDone={() => {
            setDialog(null)
            onChanged()
          }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'fail' && (
        <ReasonDialog
          title={t('pay.markFailed')}
          label={t('common.reason')}
          intro={<p className="muted">{t('pay.failIntro')}</p>}
          submitLabel={t('pay.markFailed')}
          maxLength={300}
          onSubmit={(reason) => api.finance.failPayment(clinicId, payment.id, reason)}
          onDone={() => {
            setDialog(null)
            onChanged()
          }}
          onClose={() => setDialog(null)}
        />
      )}
    </div>
  )
}

function RefundDialog({ payment, onDone, onClose }: { payment: PaymentDto; onDone: () => void; onClose: () => void }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const keeper = useIdempotencyKeeper()
  const [amount, setAmount] = useState(amountForInput(payment.amount.amount))
  const [reason, setReason] = useState('')
  const [touched, setTouched] = useState(false)
  const refund = useAction((body: RefundBody) => api.finance.refund(clinicId, payment.id, body, keeper.keyFor(body)))
  const parsed = parseAmount(amount)
  const amountError = parsed === null ? t('valid.amount') : !isPositive(parsed) ? t('valid.amountPositive') : compareAmounts(parsed, payment.amount.amount) > 0 ? t('err.finance.refund_exceeds_paid') : null
  const reasonError = reason.trim().length < 3 ? t('valid.minLength', { min: 3 }) : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (amountError || reasonError || !parsed) return
    const r = await refund.run({ amount: uzs(parsed), reason: reason.trim() })
    if (r.ok) {
      keeper.reset()
      onDone()
    } else keeper.afterError(r.error)
  }

  return (
    <Dialog title={t('pay.refund')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <p className="muted">{t('pay.refundIntro', { amount: formatMoney(payment.amount, locale) })}</p>
        <Field label={t('pay.amount')} error={touched ? amountError : null}>
          {(p) => <input {...p} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />}
        </Field>
        <Field label={t('common.reason')} error={touched ? reasonError : null}>
          {(p) => <textarea {...p} rows={2} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} />}
        </Field>
        <FormError error={refund.error} />
        <div className="row">
          <SubmitButton pending={refund.pending} className="btn btn--danger">
            {t('pay.refund')}
          </SubmitButton>
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {t('common.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
