import { ClipboardList } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import { appointmentStatuses, type AppointmentStatus } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatDateTime } from '../../lib/format'
import { formatPhone } from '../../lib/phone'
import { addDays, localDate, localRangeToUtc } from '../../lib/time'
import { Pagination } from '../../ui/Pagination'
import { SelectField } from '../../ui/Select'
import { DateField } from '../../ui/DatePicker'
import { EmptyState, QueryView } from '../../ui/states'
import { PageHeader, StatusBadge } from '../../ui/widgets'
import { useBranchParam } from './branchParam'
import { useClinic } from './clinicContext'
import { useNow } from '../../hooks/useNow'

export function AppointmentsPage() {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.appointments'))
  const api = useApi()
  const { clinicId, timeZoneFor } = useClinic()
  const [branch] = useBranchParam()
  const [params, setParams] = useSearchParams()
  const tz = timeZoneFor(branch)
  const today = localDate(now, tz)
  const from = params.get('from') ?? today
  const to = params.get('to') ?? addDays(today, 7)
  const statusParam = params.get('status') as AppointmentStatus | null
  const status = statusParam && appointmentStatuses.includes(statusParam) ? statusParam : undefined
  const doctorId = params.get('doctor') ?? undefined
  const page = Number(params.get('page')) || 1
  const range = localRangeToUtc(from, to, tz)
  const filters = { branchId: branch, status, doctorId, page, ...range }

  const doctors = useQuery(`doctors:${clinicId}`, () => api.clinic.doctors(clinicId))
  const list = useQuery(`appointments:${clinicId}:${JSON.stringify(filters)}`, () => api.clinic.appointments(clinicId, filters))

  function set(key: string, value: string | null) {
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (value) next.set(key, value)
      else next.delete(key)
      if (key !== 'page') next.delete('page')
      return next
    })
  }

  return (
    <div className="stack">
      <PageHeader icon={ClipboardList} title={t('clinicNav.appointments')} subtitle={t('appts.tzNote', { tz })} />
      <div className="filters">
        <DateField label={t('common.from')} value={from} today={today} onChange={(v) => set('from', v)} />
        <DateField label={t('common.to')} value={to} min={from} today={today} onChange={(v) => set('to', v)} />
        <SelectField
          label={t('common.status')}
          value={status ?? ''}
          onChange={(v) => set('status', v || null)}
          options={[{ value: '', label: t('common.all') }, ...appointmentStatuses.map((s) => ({ value: s, label: t(`status.${s}`) }))]}
        />
        <SelectField
          label={t('common.doctor')}
          value={doctorId ?? ''}
          onChange={(v) => set('doctor', v || null)}
          options={[{ value: '', label: t('common.all') }, ...(doctors.data ?? []).map((d) => ({ value: d.id, label: d.displayName }))]}
        />
      </div>
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState title={t('appts.empty')} icon={ClipboardList} />
          ) : (
            <>
              <div className="table-wrap">
                <table className="table table--stack">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.time')}</th>
                      <th scope="col">{t('common.patient')}</th>
                      <th scope="col">{t('common.service')}</th>
                      <th scope="col">{t('common.doctor')}</th>
                      <th scope="col">{t('common.branch')}</th>
                      <th scope="col">{t('common.status')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((a) => (
                      <tr key={a.id}>
                        <td data-label={t('common.time')}>
                          <Link to={`/clinic/${clinicId}/appointments/${a.id}`} className="nowrap">
                            {formatDateTime(a.startsAt, a.timeZone, locale)}
                          </Link>
                        </td>
                        <td data-label={t('common.patient')}>
                          {a.contactName}
                          <div className="muted small nowrap">{formatPhone(a.contactPhone)}</div>
                        </td>
                        <td data-label={t('common.service')}>{a.serviceName}</td>
                        <td data-label={t('common.doctor')}>{a.doctor?.name ?? '—'}</td>
                        <td data-label={t('common.branch')}>{a.branchName}</td>
                        <td data-label={t('common.status')}>
                          <StatusBadge status={a.status} />
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
