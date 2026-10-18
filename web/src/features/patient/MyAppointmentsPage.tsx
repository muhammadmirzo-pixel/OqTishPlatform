import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import { appointmentStatuses, type AppointmentStatus } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { Pagination } from '../../ui/Pagination'
import { ApiErrorState, EmptyState, LoadingState } from '../../ui/states'
import { PageHeader, StatusBadge } from '../../ui/widgets'
import { PendingCountdown } from '../appointments/shared'
import { appointmentWhen } from '../appointments/format'

export function MyAppointmentsPage() {
  const { t, locale } = useI18n()
  useTitle(t('appt.myTitle'))
  const api = useApi()
  const [params, setParams] = useSearchParams()
  const statusParam = params.get('status') as AppointmentStatus | null
  const status = statusParam && appointmentStatuses.includes(statusParam) ? statusParam : undefined
  const page = Number(params.get('page')) || 1
  const list = useQuery(`my-appointments:${status ?? ''}:${page}`, () => api.patient.list({ status, page }))

  return (
    <div className="page">
      <PageHeader
        title={t('appt.myTitle')}
        actions={
          <Link className="btn" to="/">
            {t('appt.bookNew')}
          </Link>
        }
      />
      <label className="field field--inline">
        <span>{t('common.status')}</span>
        <select
          value={status ?? ''}
          onChange={(e) =>
            setParams((prev) => {
              const next = new URLSearchParams(prev)
              if (e.target.value) next.set('status', e.target.value)
              else next.delete('status')
              next.delete('page')
              return next
            })
          }
        >
          <option value="">{t('common.all')}</option>
          {appointmentStatuses.map((s) => (
            <option key={s} value={s}>
              {t(`status.${s}`)}
            </option>
          ))}
        </select>
      </label>

      {list.error !== undefined ? (
        <ApiErrorState error={list.error} onRetry={list.reload} />
      ) : !list.data ? (
        <LoadingState />
      ) : list.data.items.length === 0 ? (
        <EmptyState title={t('appt.empty')}>
          <Link className="btn" to="/">
            {t('appt.bookNew')}
          </Link>
        </EmptyState>
      ) : (
        <>
          <ul className="stack list-plain">
            {list.data.items.map((a) => (
              <li key={a.id}>
                <Link to={`/me/appointments/${a.id}`} className="card card--link appt-card">
                  <div className="row row--between">
                    <strong>{a.serviceName}</strong>
                    <StatusBadge status={a.status} />
                  </div>
                  <span>
                    {a.clinicName} · {a.branchName}
                  </span>
                  <span className="muted">{appointmentWhen(a, locale)}</span>
                  {a.status === 'Pending' && a.expiresAt && <PendingCountdown expiresAt={a.expiresAt} />}
                </Link>
              </li>
            ))}
          </ul>
          <Pagination
            page={list.data.page}
            pageSize={list.data.pageSize}
            totalCount={list.data.totalCount}
            onPage={(p) =>
              setParams((prev) => {
                const next = new URLSearchParams(prev)
                next.set('page', String(p))
                return next
              })
            }
          />
        </>
      )}
    </div>
  )
}
