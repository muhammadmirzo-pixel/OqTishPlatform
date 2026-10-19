import { CalendarDays, ChevronLeft, ChevronRight, List, Rows3 } from 'lucide-react'
import type { CSSProperties, ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import type { BranchDto, CalendarBlock, CalendarDto } from '../../api/types'
import { useNow } from '../../hooks/useNow'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import type { MessageKey } from '../../i18n/messages'
import { formatDate, formatLocalDay, formatTime } from '../../lib/format'
import { addDays, fromMinutes, isValidDate, localDate, localMinutes, toMinutes, weekdayOf } from '../../lib/time'
import { Skeleton } from '../../ui/Skeleton'
import { SelectField } from '../../ui/Select'
import { ApiErrorState, EmptyState } from '../../ui/states'
import { PageHeader, StatusBadge } from '../../ui/widgets'
import { useBranchParam } from './branchParam'
import { layoutLanes, visibleRange, type Span } from './calendarLayout'
import { useClinic } from './clinicContext'

const PX_PER_MIN = 1.4
/** Shorter blocks get a one-line layout (start time + title) so the title is not clipped. */
const COMPACT_MINUTES = 45
const DAY_MINUTES = 24 * 60
type View = 'day' | 'week' | 'list'
const views: { id: View; label: MessageKey; icon: typeof List }[] = [
  { id: 'day', label: 'cal.viewDay', icon: Rows3 },
  { id: 'week', label: 'cal.viewWeek', icon: CalendarDays },
  { id: 'list', label: 'cal.viewList', icon: List },
]

/** A block placed on the branch-local day: minutes since midnight (ends past midnight are clamped). */
interface Placed {
  block: CalendarBlock
  doctor: string
  start: number
  end: number
}

function place(block: CalendarBlock, doctor: string, tz: string): Placed {
  const start = localMinutes(block.startsAt, tz)
  const end = localMinutes(block.endsAt, tz) || DAY_MINUTES
  return { block, doctor, start, end: Math.max(end, start + 1) }
}

/** CSS class suffix and title for a block. Other-branch time shows no details (the server sends none). */
function blockKind(b: CalendarBlock): string {
  if (b.otherBranch) return 'Other'
  if (b.kind === 'Appointment') return b.status ?? 'Confirmed'
  return b.kind
}

function hoursSpans(hours: { start: string; end: string }[]): Span[] {
  return hours.map((h) => ({ start: toMinutes(h.start), end: toMinutes(h.end) || DAY_MINUTES }))
}

/** Calendar: day (doctor columns), week (7 day columns, 7 requests — no range endpoint yet, D6) and list. */
export function CalendarPage() {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.calendar'))
  const { branches, timeZoneFor } = useClinic()
  const [branchParam, setBranch] = useBranchParam()
  const [params, setParams] = useSearchParams()
  const branch = branches.find((b) => b.id === branchParam) ?? branches[0]
  const tz = timeZoneFor(branch?.id)
  const today = localDate(now, tz)
  const dateParam = params.get('date')
  const date = dateParam && isValidDate(dateParam) ? dateParam : today
  const viewParam = params.get('view')
  const view: View = viewParam === 'week' || viewParam === 'list' ? viewParam : 'day'
  const step = view === 'week' ? 7 : 1

  const set = (patch: Record<string, string>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(patch)) next.set(k, v)
        return next
      },
      { replace: true },
    )

  if (!branch) return <EmptyState title={t('cal.noBranches')} icon={CalendarDays} />

  const weekStart = addDays(date, 1 - weekdayOf(date))
  const range =
    view === 'week'
      ? `${formatLocalDay(weekStart, locale).day} – ${formatLocalDay(addDays(weekStart, 6), locale).day}`
      : formatDate(date, locale)

  return (
    <div className="stack">
      <PageHeader icon={CalendarDays} title={t('clinicNav.calendar')} subtitle={`${branch.name} · ${tz}`} />
      <div className="cal-toolbar">
        {!branchParam && branches.length > 1 && (
          <SelectField inline label={t('common.branch')} value={branch.id} onChange={setBranch} options={branches.map((b) => ({ value: b.id, label: b.name }))} />
        )}
        <div className="segmented" role="group" aria-label={t('cal.view')}>
          {views.map((v) => (
            <button key={v.id} type="button" aria-pressed={view === v.id} onClick={() => set({ view: v.id })}>
              <v.icon className="icon" aria-hidden="true" />
              {t(v.label)}
            </button>
          ))}
        </div>
        <div className="cal-toolbar__nav">
          <button
            type="button"
            className="btn btn--ghost btn--small btn--icon"
            aria-label={t(view === 'week' ? 'cal.prevWeek' : 'cal.prev')}
            title={t(view === 'week' ? 'cal.prevWeek' : 'cal.prev')}
            onClick={() => set({ date: addDays(date, -step) })}
          >
            <ChevronLeft className="icon" aria-hidden="true" />
          </button>
          <button type="button" className="btn btn--ghost btn--small" onClick={() => set({ date: today })} disabled={date === today}>
            {t('cal.today')}
          </button>
          <button
            type="button"
            className="btn btn--ghost btn--small btn--icon"
            aria-label={t(view === 'week' ? 'cal.nextWeek' : 'cal.next')}
            title={t(view === 'week' ? 'cal.nextWeek' : 'cal.next')}
            onClick={() => set({ date: addDays(date, step) })}
          >
            <ChevronRight className="icon" aria-hidden="true" />
          </button>
          <label className="field field--inline">
            <span className="visually-hidden">{t('common.date')}</span>
            <input type="date" value={date} onChange={(e) => e.target.value && set({ date: e.target.value })} />
          </label>
        </div>
        <p className="cal-toolbar__range" aria-live="polite">
          {range}
        </p>
      </div>
      {view === 'week' ? (
        <WeekView branch={branch} weekStart={weekStart} today={today} now={now} onOpenDay={(d) => set({ date: d, view: 'day' })} />
      ) : (
        <DayData branch={branch} date={date} today={today} now={now} view={view} />
      )}
      {view !== 'list' && <Legend />}
    </div>
  )
}

function DayData({ branch, date, today, now, view }: { branch: BranchDto; date: string; today: string; now: number; view: 'day' | 'list' }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const calendar = useQuery(`calendar:${clinicId}:${branch.id}:${date}`, () => api.clinic.calendar(clinicId, branch.id, date), { refreshMs: 60000 })
  const closed = !branch.hours.some((h) => h.weekday === weekdayOf(date))

  return (
    <>
      {closed && <p className="muted">{t('cal.closedDay')}</p>}
      {calendar.data ? (
        calendar.data.doctors.length === 0 ? (
          <EmptyState title={t('cal.noDoctors')} icon={CalendarDays} />
        ) : view === 'list' ? (
          <DayList cal={calendar.data} />
        ) : (
          <DayGrid cal={calendar.data} branch={branch} nowMinute={date === today ? localMinutes(now, calendar.data.timeZone) : null} />
        )
      ) : calendar.error !== undefined ? (
        <ApiErrorState error={calendar.error} onRetry={calendar.reload} />
      ) : (
        <CalendarSkeleton />
      )}
    </>
  )
}

function CalendarSkeleton() {
  const { t } = useI18n()
  return (
    <div role="status" aria-live="polite">
      <span className="visually-hidden">{t('common.loading')}</span>
      <Skeleton variant="block" height={480} />
    </div>
  )
}

/** Shared time grid: hour axis on the left, one column per entry, working-hours background and the "now" line. */
function TimeGrid({
  range,
  nowMinute,
  columns,
  label,
  week = false,
}: {
  week?: boolean
  range: Span
  nowMinute: number | null
  label: string
  /** `today: false` hides the "now" line in that column (week view); day columns leave it unset. */
  columns: { key: string; head: ReactNode; today?: boolean; working: Span[]; items: Placed[]; renderItem: (p: Placed) => ReactNode; empty?: ReactNode }[]
}) {
  const { t } = useI18n()
  const height = (range.end - range.start) * PX_PER_MIN
  const y = (m: number) => (m - range.start) * PX_PER_MIN
  const ticks = Array.from({ length: (range.end - range.start) / 60 + 1 }, (_, i) => range.start + i * 60)
  const showNow = nowMinute !== null && nowMinute >= range.start && nowMinute <= range.end

  return (
    <div className={week ? 'calendar calendar--week' : 'calendar'} role="region" aria-label={label} tabIndex={0}>
      <div className="calendar__axis" aria-hidden="true">
        <div className="calendar__corner" />
        <div className="calendar__axis-body" style={{ height }}>
          {ticks.map((m) => (
            <span key={m} className={m === range.start ? 'calendar__tick calendar__tick--first' : m === range.end ? 'calendar__tick calendar__tick--last' : 'calendar__tick'} style={{ top: y(m) }}>
              {fromMinutes(m % DAY_MINUTES)}
            </span>
          ))}
          {showNow && (
            <span className="calendar__now-label" style={{ top: y(nowMinute) }}>
              {fromMinutes(nowMinute)}
            </span>
          )}
        </div>
      </div>
      {columns.map((col) => {
        const lanes = layoutLanes(col.items)
        return (
          <section key={col.key} className={col.today ? 'calendar__col calendar__col--today' : 'calendar__col'}>
            <h3 className="calendar__head">{col.head}</h3>
            <div className="calendar__body" style={{ height }}>
              {col.working.map((w, i) => (
                <span key={i} className="calendar__work" style={{ top: y(Math.max(w.start, range.start)), height: (Math.min(w.end, range.end) - Math.max(w.start, range.start)) * PX_PER_MIN }} aria-hidden="true" />
              ))}
              {ticks.map((m) => (
                <span key={m} className="calendar__line" style={{ top: y(m) }} aria-hidden="true" />
              ))}
              {col.items.length === 0 && col.empty}
              <ul className="list-plain">
                {col.items.map((p, i) => {
                  const { lane, lanes: count } = lanes[i]!
                  const style: CSSProperties = {
                    top: y(p.start),
                    height: Math.max((p.end - p.start) * PX_PER_MIN, 22),
                    left: `calc(${(lane / count) * 100}% + 3px)`,
                    width: `calc(${100 / count}% - 6px)`,
                  }
                  return (
                    <li key={`${p.block.startsAt}-${i}`} className={`calendar__block calendar__block--${blockKind(p.block)}`} style={style}>
                      {col.renderItem(p)}
                    </li>
                  )
                })}
              </ul>
              {showNow && col.today !== false && <span className="calendar__now" style={{ top: y(nowMinute) }} role="img" aria-label={t('cal.now', { time: fromMinutes(nowMinute) })} />}
            </div>
          </section>
        )
      })}
    </div>
  )
}

function BlockContent({ p, tz }: { p: Placed; tz: string }) {
  const { t, locale } = useI18n()
  const { clinicId } = useClinic()
  const b = p.block
  const title = b.otherBranch ? t('cal.otherBranch') : b.kind === 'TimeOff' ? t('cal.timeOff') : b.kind === 'Hold' && !b.serviceName ? t('cal.hold') : (b.serviceName ?? '')
  const start = formatTime(b.startsAt, tz, locale)
  const content =
    p.end - p.start < COMPACT_MINUTES ? (
      <>
        <span className="calendar__line1">
          <span className="calendar__time">{start}</span> <span className="calendar__title">{title}</span>
        </span>
        {b.contactName && <span className="calendar__who">{b.contactName}</span>}
      </>
    ) : (
      <>
        <span className="calendar__time">
          {start}–{formatTime(b.endsAt, tz, locale)}
        </span>
        <span className="calendar__title">{title}</span>
        {b.contactName && <span className="calendar__who">{b.contactName}</span>}
      </>
    )
  return b.appointmentId ? <Link to={`/clinic/${clinicId}/appointments/${b.appointmentId}`}>{content}</Link> : content
}

function DayGrid({ cal, branch, nowMinute }: { cal: CalendarDto; branch: BranchDto; nowMinute: number | null }) {
  const { t } = useI18n()
  const tz = cal.timeZone
  const branchHours = branch.hours.filter((h) => h.weekday === weekdayOf(cal.date)).map((h) => ({ start: toMinutes(h.open), end: toMinutes(h.close) || DAY_MINUTES }))
  const columns = cal.doctors.map((d) => ({ d, working: hoursSpans(d.workingHours), items: d.blocks.map((b) => place(b, d.name, tz)) }))
  const range = visibleRange([...branchHours, ...columns.flatMap((c) => [...c.working, ...c.items])])

  return (
    <TimeGrid
      label={t('clinicNav.calendar')}
      range={range}
      nowMinute={nowMinute}
      columns={columns.map((c) => ({
        key: c.d.id,
        head: c.d.name,
        working: c.working,
        items: c.items,
        renderItem: (p) => <BlockContent p={p} tz={tz} />,
        empty: <p className="calendar__free">{t('cal.free')}</p>,
      }))}
    />
  )
}

function WeekView({ branch, weekStart, today, now, onOpenDay }: { branch: BranchDto; weekStart: string; today: string; now: number; onOpenDay: (date: string) => void }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [params, setParams] = useSearchParams()
  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i))
  const week = useQuery(`calendar-week:${clinicId}:${branch.id}:${weekStart}`, () => Promise.all(days.map((d) => api.clinic.calendar(clinicId, branch.id, d))), { refreshMs: 60000 })

  if (!week.data) return week.error !== undefined ? <ApiErrorState error={week.error} onRetry={week.reload} /> : <CalendarSkeleton />

  // One doctor per week: across doctors the day view (doctor columns) is the readable layout.
  const doctors = week.data[0]?.doctors ?? []
  if (doctors.length === 0) return <EmptyState title={t('cal.noDoctors')} icon={CalendarDays} />
  const doctorId = doctors.some((d) => d.id === params.get('doctor')) ? params.get('doctor')! : doctors[0]!.id
  const tz = week.data[0]?.timeZone ?? branch.timeZone
  const columns = week.data.map((cal) => {
    const d = cal.doctors.find((x) => x.id === doctorId)
    return { cal, working: hoursSpans(d?.workingHours ?? []), items: (d?.blocks ?? []).map((b) => place(b, d!.name, tz)) }
  })
  const range = visibleRange(columns.flatMap((c) => [...c.working, ...c.items]))

  return (
    <>
      {doctors.length > 1 && (
        <SelectField
          inline
          label={t('common.doctor')}
          value={doctorId}
          onChange={(v) =>
            setParams(
              (prev) => {
                const next = new URLSearchParams(prev)
                next.set('doctor', v)
                return next
              },
              { replace: true },
            )
          }
          options={doctors.map((d) => ({ value: d.id, label: d.name }))}
        />
      )}
      <TimeGrid
        week
        label={t('cal.viewWeek')}
        range={range}
        nowMinute={days.includes(today) ? localMinutes(now, tz) : null}
        columns={columns.map((c) => {
          const day = formatLocalDay(c.cal.date, locale)
          const isToday = c.cal.date === today
          return {
            key: c.cal.date,
            today: isToday,
            head: (
              <button type="button" className="calendar__day" onClick={() => onOpenDay(c.cal.date)} aria-label={t('cal.openDay', { day: `${day.weekday}, ${day.day}` })}>
                <span>{day.weekday}</span> {day.day}
              </button>
            ),
            working: c.working,
            items: c.items,
            renderItem: (p) => <BlockContent p={p} tz={tz} />,
          }
        })}
      />
    </>
  )
}

function DayList({ cal }: { cal: CalendarDto }) {
  const { t, locale } = useI18n()
  const { clinicId } = useClinic()
  const rows = cal.doctors.flatMap((d) => d.blocks.map((b) => place(b, d.name, cal.timeZone))).sort((a, b) => a.start - b.start || a.doctor.localeCompare(b.doctor))
  if (rows.length === 0) return <EmptyState title={t('cal.emptyDay')} icon={CalendarDays} />
  return (
    <div className="table-wrap">
      <table className="table table--stack">
        <thead>
          <tr>
            <th scope="col">{t('common.time')}</th>
            <th scope="col">{t('common.doctor')}</th>
            <th scope="col">{t('cal.what')}</th>
            <th scope="col">{t('common.patient')}</th>
            <th scope="col">{t('common.status')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((p, i) => {
            const b = p.block
            const time = `${formatTime(b.startsAt, cal.timeZone, locale)}–${formatTime(b.endsAt, cal.timeZone, locale)}`
            return (
              <tr key={`${b.startsAt}-${i}`}>
                <td data-label={t('common.time')} className="tabular">
                  {b.appointmentId ? <Link to={`/clinic/${clinicId}/appointments/${b.appointmentId}`}>{time}</Link> : time}
                </td>
                <td data-label={t('common.doctor')}>{p.doctor}</td>
                <td data-label={t('cal.what')}>{b.otherBranch ? t('cal.otherBranch') : b.kind === 'TimeOff' ? t('cal.timeOff') : (b.serviceName ?? t('cal.hold'))}</td>
                <td data-label={t('common.patient')}>{b.contactName ?? '—'}</td>
                <td data-label={t('common.status')}>{b.status ? <StatusBadge status={b.status} /> : '—'}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function Legend() {
  const { t } = useI18n()
  const items: [string, string][] = [
    ['Confirmed', t('status.Confirmed')],
    ['Hold', t('cal.hold')],
    ['Completed', t('status.Completed')],
    ['NoShow', t('status.NoShow')],
    ['TimeOff', t('cal.timeOff')],
    ['Other', t('cal.otherBranch')],
    ['off', t('cal.offHours')],
  ]
  return (
    <div className="cal-legend">
      <p className="cal-legend__title">{t('cal.legendTitle')}</p>
      <ul className="list-plain cal-legend__list">
        {items.map(([kind, label]) => (
          <li key={kind}>
            <span className={`cal-legend__swatch calendar__block--${kind}`} aria-hidden="true" />
            {label}
          </li>
        ))}
      </ul>
    </div>
  )
}
