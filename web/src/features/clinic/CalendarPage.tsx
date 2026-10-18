import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import type { CalendarBlock } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatDate, formatTime } from '../../lib/format'
import { addDays, fromMinutes, isValidDate, localDate, localMinutes, toMinutes, weekdayOf } from '../../lib/time'
import { EmptyState, QueryView } from '../../ui/states'
import { PageHeader } from '../../ui/widgets'
import { useBranchParam } from './branchParam'
import { useClinic } from './clinicContext'
import { useNow } from '../../hooks/useNow'

const PX_PER_MIN = 1.2
const DEFAULT_START = 8 * 60
const DEFAULT_END = 20 * 60

/** Day view: doctors as columns, blocks positioned by branch-local time. */
export function CalendarPage() {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.calendar'))
  const api = useApi()
  const { clinicId, branches, timeZoneFor } = useClinic()
  const [branchParam, setBranch] = useBranchParam()
  const [params, setParams] = useSearchParams()
  const branch = branches.find((b) => b.id === branchParam) ?? branches[0]
  const tz = timeZoneFor(branch?.id)
  const today = localDate(now, tz)
  const dateParam = params.get('date')
  const date = dateParam && isValidDate(dateParam) ? dateParam : today
  const calendar = useQuery(branch ? `calendar:${clinicId}:${branch.id}:${date}` : null, () => api.clinic.calendar(clinicId, branch!.id, date), { refreshMs: 60000 })

  const setDate = (d: string) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      next.set('date', d)
      return next
    })

  if (!branch) return <EmptyState title={t('cal.noBranches')} />

  const hours = branch.hours.filter((h) => h.weekday === weekdayOf(date))
  const blocks = calendar.data?.doctors.flatMap((d) => d.blocks) ?? []
  const starts = [...hours.map((h) => toMinutes(h.open)), ...blocks.map((b) => localMinutes(b.startsAt, tz))]
  const ends = [...hours.map((h) => toMinutes(h.close)), ...blocks.map((b) => localMinutes(b.endsAt, tz) || 24 * 60)]
  const dayStart = Math.floor((starts.length ? Math.min(...starts) : DEFAULT_START) / 60) * 60
  const dayEnd = Math.ceil((ends.length ? Math.max(...ends) : DEFAULT_END) / 60) * 60
  const height = (dayEnd - dayStart) * PX_PER_MIN
  const ticks = Array.from({ length: (dayEnd - dayStart) / 60 + 1 }, (_, i) => dayStart + i * 60)

  return (
    <div className="stack">
      <PageHeader title={t('clinicNav.calendar')} subtitle={`${branch.name} · ${formatDate(date, locale)} · ${tz}`} />
      <div className="row">
        {!branchParam && branches.length > 1 && (
          <label className="field field--inline">
            <span>{t('common.branch')}</span>
            <select value={branch.id} onChange={(e) => setBranch(e.target.value)}>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </select>
          </label>
        )}
        <button type="button" className="btn btn--ghost btn--small" onClick={() => setDate(addDays(date, -1))}>
          ← {t('cal.prev')}
        </button>
        <button type="button" className="btn btn--ghost btn--small" onClick={() => setDate(today)} disabled={date === today}>
          {t('cal.today')}
        </button>
        <button type="button" className="btn btn--ghost btn--small" onClick={() => setDate(addDays(date, 1))}>
          {t('cal.next')} →
        </button>
        <label className="field field--inline">
          <span className="visually-hidden">{t('common.date')}</span>
          <input type="date" value={date} onChange={(e) => e.target.value && setDate(e.target.value)} />
        </label>
      </div>
      {hours.length === 0 && <p className="muted">{t('cal.closedDay')}</p>}
      <QueryView query={calendar}>
        {(cal) =>
          cal.doctors.length === 0 ? (
            <EmptyState title={t('cal.noDoctors')} />
          ) : (
            <div className="calendar" role="region" aria-label={t('clinicNav.calendar')} tabIndex={0}>
              <div className="calendar__axis" style={{ height }} aria-hidden="true">
                {ticks.map((m) => (
                  <span key={m} className="calendar__tick" style={{ top: (m - dayStart) * PX_PER_MIN }}>
                    {fromMinutes(m % (24 * 60))}
                  </span>
                ))}
              </div>
              {cal.doctors.map((d) => (
                <section key={d.id} className="calendar__col" aria-label={d.name}>
                  <h3 className="calendar__head">{d.name}</h3>
                  <div className="calendar__body" style={{ height }}>
                    {ticks.map((m) => (
                      <span key={m} className="calendar__line" style={{ top: (m - dayStart) * PX_PER_MIN }} aria-hidden="true" />
                    ))}
                    {d.blocks.length === 0 && <p className="calendar__free">{t('cal.free')}</p>}
                    <ul className="list-plain">
                      {d.blocks.map((b, i) => (
                        <Block key={`${b.startsAt}-${i}`} block={b} top={(localMinutes(b.startsAt, tz) - dayStart) * PX_PER_MIN} minutes={(new Date(b.endsAt).getTime() - new Date(b.startsAt).getTime()) / 60000} clinicId={clinicId} tz={tz} />
                      ))}
                    </ul>
                  </div>
                </section>
              ))}
            </div>
          )
        }
      </QueryView>
      <p className="muted small">{t('cal.legend')}</p>
    </div>
  )
}

function Block({ block, top, minutes, clinicId, tz }: { block: CalendarBlock; top: number; minutes: number; clinicId: string; tz: string }) {
  const { t, locale } = useI18n()
  const label = `${formatTime(block.startsAt, tz, locale)}–${formatTime(block.endsAt, tz, locale)}`
  const kind = block.kind === 'Appointment' ? (block.status ?? 'Confirmed') : block.kind
  const content = (
    <>
      <span className="calendar__time">{label}</span>
      <span className="calendar__title">
        {block.kind === 'TimeOff' ? t('cal.timeOff') : block.kind === 'Hold' ? t('cal.hold') : (block.serviceName ?? '')}
      </span>
      {block.contactName && <span className="calendar__who">{block.contactName}</span>}
    </>
  )
  return (
    <li className={`calendar__block calendar__block--${kind}`} style={{ top, height: Math.max(minutes * PX_PER_MIN, 22) }}>
      {block.appointmentId ? <Link to={`/clinic/${clinicId}/appointments/${block.appointmentId}`}>{content}</Link> : content}
    </li>
  )
}
