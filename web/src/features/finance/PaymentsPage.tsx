import { Wallet } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatDateTime, formatFullDateTime, formatMoney } from '../../lib/format'
import { addDays, localDate, localRangeToUtc } from '../../lib/time'
import { Pagination } from '../../ui/Pagination'
import { SelectField } from '../../ui/Select'
import { DateField } from '../../ui/DatePicker'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { useBranchParam } from '../clinic/branchParam'
import { useClinic } from '../clinic/clinicContext'
import { Perm } from '../clinic/permissions'
import { PaymentActions } from './PaymentActions'
import { RecordPaymentForm } from './RecordPaymentForm'
import { useNow } from '../../hooks/useNow'

export function PaymentsPage() {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.payments'))
  const api = useApi()
  const { clinicId, can, timeZoneFor } = useClinic()
  const [branch] = useBranchParam()
  const [params, setParams] = useSearchParams()
  const tz = timeZoneFor(branch)
  const today = localDate(now, tz)
  const from = params.get('from') ?? addDays(today, -6)
  const to = params.get('to') ?? today
  const page = Number(params.get('page')) || 1
  const filters = { branchId: branch, page, ...localRangeToUtc(from, to, tz) }
  const list = useQuery(`payments:${clinicId}:${JSON.stringify(filters)}`, () => api.finance.payments(clinicId, filters))

  const set = (key: string, value: string) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      next.set(key, value)
      if (key !== 'page') next.delete('page')
      return next
    })

  return (
    <div className="stack">
      <PageHeader icon={Wallet} title={t('clinicNav.payments')} subtitle={t('pay.serverNote')} />
      {can(Perm.paymentRecord) && <QuickRecord onRecorded={list.reload} />}
      <div className="filters">
        <DateField label={t('common.from')} value={from} today={today} onChange={(v) => set('from', v)} />
        <DateField label={t('common.to')} value={to} min={from} today={today} onChange={(v) => set('to', v)} />
      </div>
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState title={t('pay.none')} icon={Wallet} />
          ) : (
            <>
              <div className="table-wrap">
                <table className="table table--stack">
                  <thead>
                    <tr>
                      <th scope="col">{t('pay.when')}</th>
                      <th scope="col">{t('common.patient')}</th>
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
                    {data.items.map((p) => (
                      <tr key={p.id}>
                        <td data-label={t('pay.when')}>{formatFullDateTime(p.recordedAt, timeZoneFor(p.branchId), locale)}</td>
                        <td data-label={t('common.patient')}>
                          {can(Perm.appointmentView) ? <Link to={`/clinic/${clinicId}/appointments/${p.appointmentId}`}>{p.contactName ?? t('pay.appointment')}</Link> : (p.contactName ?? '—')}
                          {p.serviceName && <div className="muted small">{p.serviceName}</div>}
                        </td>
                        <td data-label={t('pay.type')}>{t(`pay.${p.direction}`)}</td>
                        <td data-label={t('pay.method')}>
                          {t(`pay.${p.method}`)}
                          {p.externalRef && <div className="muted small">{p.externalRef}</div>}
                        </td>
                        <td className="num" data-label={t('pay.amount')}>
                          {formatMoney(p.amount, locale)}
                        </td>
                        <td data-label={t('common.status')}>
                          <Badge tone={p.status === 'Recorded' ? 'success' : 'neutral'}>{t(`pay.${p.status}`)}</Badge>
                        </td>
                        <td data-label={t('common.actions')}>
                          <PaymentActions payment={p} onChanged={list.reload} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPage={(p) => set('page', String(p))} />
            </>
          )
        }
      </QueryView>
    </div>
  )
}

/** Pick one of today's / recent visits and record its payment without leaving the page. */
function QuickRecord({ onRecorded }: { onRecorded: () => void }) {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId, can, timeZoneFor } = useClinic()
  const [branch] = useBranchParam()
  const [appointmentId, setAppointmentId] = useState('')
  const tz = timeZoneFor(branch)
  const today = localDate(now, tz)
  const range = localRangeToUtc(addDays(today, -2), today, tz)
  const recent = useQuery(can(Perm.appointmentView) ? `recent-visits:${clinicId}:${branch ?? ''}:${today}` : null, () => api.clinic.appointments(clinicId, { branchId: branch, ...range, pageSize: 100 }))
  const balance = useQuery(appointmentId ? `balance:${appointmentId}` : null, () => api.finance.balance(clinicId, appointmentId))
  const visits = (recent.data?.items ?? []).filter((a) => a.status === 'Confirmed' || a.status === 'Completed' || a.status === 'NoShow')

  if (!can(Perm.appointmentView)) return <p className="muted small">{t('pay.openAppointmentHint')}</p>

  return (
    <section className="card stack" aria-labelledby="quick-pay-title">
      <h2 id="quick-pay-title">{t('pay.quickTitle')}</h2>
      <SelectField
        label={t('pay.appointment')}
        value={appointmentId}
        onChange={setAppointmentId}
        options={[
          { value: '', label: visits.length ? t('pay.chooseVisit') : t('pay.noVisits') },
          ...visits.map((a) => ({ value: a.id, label: `${formatDateTime(a.startsAt, a.timeZone, locale)} · ${a.contactName} · ${a.serviceName}` })),
        ]}
      />
      {appointmentId && balance.data && (
        <RecordPaymentForm
          key={`${appointmentId}:${balance.data.outstanding.amount}`}
          clinicId={clinicId}
          appointmentId={appointmentId}
          outstanding={balance.data.outstanding}
          onRecorded={() => {
            balance.reload()
            onRecorded()
          }}
        />
      )}
    </section>
  )
}
