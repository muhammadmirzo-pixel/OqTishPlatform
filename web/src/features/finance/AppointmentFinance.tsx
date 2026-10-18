import { useApi } from '../../api/context'
import type { AppointmentDto } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime, formatMoney } from '../../lib/format'
import { QueryView } from '../../ui/states'
import { Badge, Details } from '../../ui/widgets'
import { useClinic } from '../clinic/clinicContext'
import { Perm } from '../clinic/permissions'
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
              [t('pay.charged'), formatMoney(b.charged, locale)],
              [t('pay.paid'), formatMoney(b.paid, locale)],
              [t('pay.refunded'), formatMoney(b.refunded, locale)],
              [t('pay.outstanding'), <strong key="o">{formatMoney(b.outstanding, locale)}</strong>],
            ]}
          />
        )}
      </QueryView>
      {own.length > 0 && (
        <div className="table-wrap">
          <table className="table">
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
                  <td>{formatFullDateTime(p.recordedAt, appointment.timeZone, locale)}</td>
                  <td>{t(`pay.${p.direction}`)}</td>
                  <td>{t(`pay.${p.method}`)}</td>
                  <td className="num">{formatMoney(p.amount, locale)}</td>
                  <td>
                    <Badge tone={p.status === 'Recorded' ? 'success' : 'neutral'}>{t(`pay.${p.status}`)}</Badge>
                  </td>
                  <td>
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
