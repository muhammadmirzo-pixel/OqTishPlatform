import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import { appointmentStatuses, type AppointmentStatus } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { Pagination } from '../../ui/Pagination'
import { CalendarDays } from 'lucide-react'
import { ApiErrorState, EmptyState } from '../../ui/states'
import { Skeleton } from '../../ui/Skeleton'
import { formatLocalDay } from '../../lib/format'
import { localDate } from '../../lib/time'
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
          <Link className="btn" to="/clinics">
            {t('appt.bookNew')}
          </Link>
        }
      />
      <div className="chip-scroll" role="group" aria-label={t('common.status')}>
        {([undefined, ...appointmentStatuses] as (AppointmentStatus | undefined)[]).map((s) => (
          <button
            key={s ?? 'all'}
            type="button"
            className="chip"
            aria-pressed={status === s}
            onClick={() =>
              setParams((prev) => {
                const next = new URLSearchParams(prev)
                if (s) next.set('status', s)
                else next.delete('status')
                next.delete('page')
                return next
              })
            }
          >
            {s ? t(`status.${s}`) : t('myAppts.all')}
          </button>
        ))}
      </div>

      {list.error !== undefined ? (
        <ApiErrorState error={list.error} onRetry={list.reload} />
      ) : !list.data ? (
        <SkeletonList label={t('common.loading')} />
      ) : list.data.items.length === 0 ? (
        <EmptyState title={t('appt.empty')} icon={CalendarDays}>
          <Link className="btn" to="/clinics">
            {t('appt.bookNew')}
          </Link>
        </EmptyState>
      ) : (
        <>
          <ul className="stack list-plain">
            {list.data.items.map((a) => (
              <li key={a.id}>
                <Link to={`/me/appointments/${a.id}`} className="card card--link appt-card">
                  <DateBlock iso={a.startsAt} timeZone={a.timeZone} />
                  <div className="appt-card__info">
                    <div className="row row--between">
                      <strong>{a.serviceName}</strong>
                      <StatusBadge status={a.status} />
                    </div>
                    <span className="muted small">
                      {a.clinicName} · {a.branchName}
                    </span>
                    <span className="small">{appointmentWhen(a, locale)}</span>
                    {a.status === 'Pending' && a.expiresAt && (
                      <span className="small appt-card__wait">
                        <PendingCountdown expiresAt={a.expiresAt} />
                      </span>
                    )}
                  </div>
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

/** Calendar-leaf date for list cards, in the branch's time zone. */
function DateBlock({ iso, timeZone }: { iso: string; timeZone: string }) {
  const { locale } = useI18n()
  const day = formatLocalDay(localDate(iso, timeZone), locale)
  const [num, month] = day.day.split(/[-\s]/)
  return (
    <span className="date-block" aria-hidden="true">
      <span className="date-block__month">{month ?? ''}</span>
      <span className="date-block__day">{num}</span>
      <span className="date-block__weekday">{day.weekday}</span>
    </span>
  )
}

function SkeletonList({ label }: { label: string }) {
  return (
    <div role="status" aria-live="polite" className="stack">
      <span className="visually-hidden">{label}</span>
      {[0, 1, 2].map((i) => (
        <div key={i} className="card appt-card" aria-hidden="true">
          <Skeleton variant="block" width={64} height={72} />
          <div className="appt-card__info">
            <Skeleton variant="title" />
            <Skeleton width="60%" />
            <Skeleton width="40%" />
          </div>
        </div>
      ))}
    </div>
  )
}
