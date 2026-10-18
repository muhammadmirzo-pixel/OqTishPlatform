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
      <PageHeader title={t('clinicNav.appointments')} subtitle={t('appts.tzNote', { tz })} />
      <div className="filters">
        <label className="field">
          <span>{t('common.from')}</span>
          <input type="date" value={from} onChange={(e) => set('from', e.target.value)} />
        </label>
        <label className="field">
          <span>{t('common.to')}</span>
          <input type="date" value={to} min={from} onChange={(e) => set('to', e.target.value)} />
        </label>
        <label className="field">
          <span>{t('common.status')}</span>
          <select value={status ?? ''} onChange={(e) => set('status', e.target.value || null)}>
            <option value="">{t('common.all')}</option>
            {appointmentStatuses.map((s) => (
              <option key={s} value={s}>
                {t(`status.${s}`)}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>{t('common.doctor')}</span>
          <select value={doctorId ?? ''} onChange={(e) => set('doctor', e.target.value || null)}>
            <option value="">{t('common.all')}</option>
            {doctors.data?.map((d) => (
              <option key={d.id} value={d.id}>
                {d.displayName}
              </option>
            ))}
          </select>
        </label>
      </div>
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState title={t('appts.empty')} />
          ) : (
            <>
              <div className="table-wrap">
                <table className="table">
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
                        <td>
                          <Link to={`/clinic/${clinicId}/appointments/${a.id}`}>{formatDateTime(a.startsAt, a.timeZone, locale)}</Link>
                        </td>
                        <td>
                          {a.contactName}
                          <div className="muted small">{formatPhone(a.contactPhone)}</div>
                        </td>
                        <td>{a.serviceName}</td>
                        <td>{a.doctor?.name ?? '—'}</td>
                        <td>{a.branchName}</td>
                        <td>
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
