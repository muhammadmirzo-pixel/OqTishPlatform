import { useApi } from '../../api/context'
import type { AppointmentDto } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime, formatMoney } from '../../lib/format'
import { QueryView } from '../../ui/states'
import { Badge, Details } from '../../ui/widgets'
import { useClinic } from '../clinic/clinicContext'
import { Perm } from '../clinic/permissions'
import { FinanceTerm } from './FinanceTerm'
import { PaymentActions } from './PaymentActions'
import { RecordPaymentForm } from './RecordPaymentForm'

const payable = new Set(['Confirmed', 'Completed', 'NoShow'])

/** Balance, payments and the record-payment form for one appointment. */
export function AppointmentFinance({ appointment }: { appointment: AppointmentDto }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId, can } = useClinic()
  const balance = useQuery(`balance:${appointment.id}`, () => api.finance.balance(clinicId, appointment.id))
  const payments = useQuery(`payments-of:${appointment.id}`, () => api.finance.payments(clinicId, { appointmentId: appointment.id, branchId: appointment.branchId, pageSize: 100 }))
  const reload = () => {
    balance.reload()
    payments.reload()
  }
  const own = payments.data?.items ?? []

  return (
    <section className="card stack" aria-labelledby="finance-title">
      <h2 id="finance-title">{t('pay.balanceTitle')}</h2>
      <QueryView query={balance}>
        {(b) => (
          <Details
            items={[
              [<FinanceTerm key="c" term="charged" />, formatMoney(b.charged, locale)],
              [<FinanceTerm key="p" term="paid" />, formatMoney(b.paid, locale)],
              [<FinanceTerm key="r" term="refunded" />, formatMoney(b.refunded, locale)],
              [<FinanceTerm key="o" term="outstanding" />, <strong key="o">{formatMoney(b.outstanding, locale)}</strong>],
            ]}
          />
        )}
      </QueryView>
      {own.length > 0 && (
        <div className="table-wrap">
          <table className="table table--stack">
            <thead>
              <tr>
                <th scope="col">{t('pay.when')}</th>
                <th scope="col">{t('pay.type')}</th>
                <th scope="col">{t('pay.method')}</th>
                <th scope="col" className="num">
                  {t('pay.amount')}
                </th>
                <th scope="col">{t('common.status')}</th>
                <th scope="col">{t('common.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {own.map((p) => (
                <tr key={p.id}>
                  <td data-label={t('pay.when')}>{formatFullDateTime(p.recordedAt, appointment.timeZone, locale)}</td>
                  <td data-label={t('pay.type')}>{t(`pay.${p.direction}`)}</td>
                  <td data-label={t('pay.method')}>{t(`pay.${p.method}`)}</td>
                  <td className="num" data-label={t('pay.amount')}>
                    {formatMoney(p.amount, locale)}
                  </td>
                  <td data-label={t('common.status')}>
                    <Badge tone={p.status === 'Recorded' ? 'success' : 'neutral'}>{t(`pay.${p.status}`)}</Badge>
                  </td>
                  <td data-label={t('common.actions')}>
                    <PaymentActions payment={p} onChanged={reload} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {can(Perm.paymentRecord) && payable.has(appointment.status) && balance.data && (
        <RecordPaymentForm key={balance.data.outstanding.amount} clinicId={clinicId} appointmentId={appointment.id} outstanding={balance.data.outstanding} onRecorded={reload} />
      )}
      <p className="muted small">{t('pay.serverNote')}</p>
    </section>
  )
}
