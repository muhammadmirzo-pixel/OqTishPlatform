import { CalendarCheck, CalendarDays, CheckCircle2, ChevronRight, Hourglass, Inbox, UserPlus, UserX, Wallet, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../api/context'
import type { DailyReportRow, DashboardDto } from '../../api/types'
import { useNow } from '../../hooks/useNow'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { useSession } from '../../auth/session'
import { formatCompactNumber, formatDate, formatLocalDay, formatMoney, formatTime } from '../../lib/format'
import { toTiyin } from '../../lib/money'
import { formatPhone } from '../../lib/phone'
import { addDays, localDate, localRangeToUtc } from '../../lib/time'
import { LineChart } from '../../ui/LineChart'
import { Skeleton } from '../../ui/Skeleton'
import { InfoTip } from '../../ui/Tooltip'
import { ApiErrorState, EmptyState } from '../../ui/states'
import { PageHeader, StatCard, StatusBadge } from '../../ui/widgets'
import { useBranchParam } from './branchParam'
import { useClinic } from './clinicContext'
import { Perm } from './permissions'

const CHART_DAYS = 14

export function DashboardPage() {
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.dashboard'))
  const api = useApi()
  const { me } = useSession()
  const { clinicId, can, branchName, timeZoneFor } = useClinic()
  const [branch] = useBranchParam()
  const now = useNow(60_000)
  const tz = timeZoneFor(branch)
  const today = localDate(now, tz)
  const dash = useQuery(`dashboard:${clinicId}:${branch ?? ''}`, () => api.clinic.dashboard(clinicId, branch), { refreshMs: 60000 })
  const suffix = branch ? `?branch=${encodeURIComponent(branch)}` : ''
  const base = `/clinic/${clinicId}`
  const name = me?.displayName?.trim()

  return (
    <div className="stack">
      <PageHeader
        title={name ? t('dash.welcome', { name }) : t('dash.welcomeAnon')}
        subtitle={`${t('console.today', { date: formatDate(today, locale) })} · ${branch ? branchName(branch) : t('common.allBranches')}`}
      />
      <DashboardStats query={dash} />
      <div className={can(Perm.appointmentView) ? 'dash-grid dash-grid--split' : 'dash-grid'}>
        {can(Perm.appointmentView) && <TodayAppointments today={today} tz={tz} />}
        <section className="card stack" aria-labelledby="quick-title">
          <h2 id="quick-title">{t('dash.quick')}</h2>
          <ul className="quick-actions">
            {can(Perm.triage) && <QuickAction to={`${base}/requests${suffix}`} icon={Inbox} tone="warning" label={t('clinicNav.requests')} count={dash.data?.pending} />}
            {can(Perm.walkIn) && <QuickAction to={`${base}/walk-in${suffix}`} icon={UserPlus} label={t('clinicNav.walkIn')} />}
            {can(Perm.appointmentView) && <QuickAction to={`${base}/calendar${suffix}`} icon={CalendarDays} tone="violet" label={t('clinicNav.calendar')} />}
            {can(Perm.paymentRecord) && <QuickAction to={`${base}/payments${suffix}`} icon={Wallet} tone="teal" label={t('clinicNav.payments')} />}
          </ul>
        </section>
      </div>
      {can(Perm.reportView) && <FinanceTrend today={today} />}
    </div>
  )
}

function Term({ label, children }: { label: string; children: ReactNode }) {
  const { t } = useI18n()
  return <InfoTip label={t('tip.about', { term: label })}>{children}</InfoTip>
}

function DashboardStats({ query }: { query: { data: DashboardDto | undefined; error: unknown; reload: () => void } }) {
  const { t, locale } = useI18n()
  const d = query.data
  if (!d && query.error !== undefined) return <ApiErrorState error={query.error} onRetry={query.reload} />
  if (!d) {
    return (
      <div className="stats" role="status" aria-live="polite">
        <span className="visually-hidden">{t('common.loading')}</span>
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="stat" aria-hidden="true">
            <Skeleton variant="circle" width={44} height={44} />
            <Skeleton width="70%" />
            <Skeleton variant="title" width="40%" />
          </div>
        ))}
      </div>
    )
  }
  return (
    <div className="stats">
      <StatCard
        label={t('dash.pending')}
        value={d.pending}
        tone={d.pending > 0 ? 'warning' : undefined}
        icon={Hourglass}
        iconTone="warning"
        info={<Term label={t('dash.pending')}>{t('dash.pendingInfo')}</Term>}
      />
      <StatCard label={t('dash.confirmed')} value={d.confirmed} icon={CalendarCheck} info={<Term label={t('dash.confirmed')}>{t('dash.confirmedInfo')}</Term>} />
      <StatCard
        label={t('dash.completed')}
        value={d.completed}
        icon={CheckCircle2}
        iconTone="teal"
        info={<Term label={t('dash.completed')}>{t('dash.completedInfo')}</Term>}
      />
      <StatCard label={t('dash.noShow')} value={d.noShow} icon={UserX} iconTone="danger" info={<Term label={t('dash.noShow')}>{t('dash.noShowInfo')}</Term>} />
      {d.collected && (
        <StatCard
          label={t('dash.collected')}
          value={<MoneyValue text={formatMoney(d.collected, locale)} />}
          icon={Wallet}
          iconTone="violet"
          info={<Term label={t('dash.collected')}>{t('dash.collectedInfo')}</Term>}
        />
      )}
    </div>
  )
}

/** "2 450 000 so‘m" → amount in the metric size, currency label smaller. */
function MoneyValue({ text }: { text: string }) {
  const cut = text.lastIndexOf(' ')
  return (
    <>
      {text.slice(0, cut)} <span className="stat__unit">{text.slice(cut + 1)}</span>
    </>
  )
}

function QuickAction({ to, icon: Icon, tone, label, count }: { to: string; icon: LucideIcon; tone?: 'teal' | 'violet' | 'warning'; label: string; count?: number }) {
  return (
    <li>
      <Link className="quick-action" to={to}>
        <span className={`icon-tile icon-tile--sm${tone ? ` icon-tile--${tone}` : ''}`}>
          <Icon className="icon" aria-hidden="true" />
        </span>
        <span className="quick-action__label">{label}</span>
        {count !== undefined && count > 0 && <span className="badge badge--warning">{count}</span>}
        <ChevronRight className="icon quick-action__chev" aria-hidden="true" />
      </Link>
    </li>
  )
}

function TodayAppointments({ today, tz }: { today: string; tz: string }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [branch] = useBranchParam()
  const filters = { branchId: branch, pageSize: 50, ...localRangeToUtc(today, today, tz) }
  const list = useQuery(`dash-today:${clinicId}:${JSON.stringify(filters)}`, () => api.clinic.appointments(clinicId, filters), { refreshMs: 60000 })
  const allLink = `/clinic/${clinicId}/appointments?from=${today}&to=${today}${branch ? `&branch=${encodeURIComponent(branch)}` : ''}`

  let body: ReactNode
  if (list.data) {
    const { items, totalCount } = list.data
    body =
      items.length === 0 ? (
        <EmptyState title={t('dash.todayEmpty')} icon={CalendarDays} />
      ) : (
        <div className="table-wrap">
          <table className="table table--stack">
            <thead>
              <tr>
                <th scope="col">{t('common.time')}</th>
                <th scope="col">{t('common.patient')}</th>
                <th scope="col">{t('common.service')}</th>
                <th scope="col">{t('common.doctor')}</th>
                <th scope="col">{t('common.status')}</th>
              </tr>
            </thead>
            <tbody>
              {items.map((a) => (
                <tr key={a.id}>
                  <td data-label={t('common.time')}>
                    <Link to={`/clinic/${clinicId}/appointments/${a.id}`} className="num">
                      {formatTime(a.startsAt, a.timeZone, locale)}–{formatTime(a.endsAt, a.timeZone, locale)}
                    </Link>
                  </td>
                  <td data-label={t('common.patient')}>
                    {a.contactName}
                    <div className="muted small nowrap">{formatPhone(a.contactPhone)}</div>
                  </td>
                  <td data-label={t('common.service')}>{a.serviceName}</td>
                  <td data-label={t('common.doctor')}>{a.doctor?.name ?? '—'}</td>
                  <td data-label={t('common.status')}>
                    <StatusBadge status={a.status} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {totalCount > items.length && (
            <p className="small">
              <Link to={allLink}>{t('dash.viewAll', { count: totalCount })}</Link>
            </p>
          )}
        </div>
      )
  } else if (list.error !== undefined) {
    body = <ApiErrorState error={list.error} onRetry={list.reload} />
  } else {
    body = (
      <div className="stack" role="status" aria-live="polite">
        <span className="visually-hidden">{t('common.loading')}</span>
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} variant="title" />
        ))}
      </div>
    )
  }

  return (
    <section className="card stack" aria-labelledby="today-title">
      <div className="section-head">
        <h2 id="today-title">{t('dash.todayTitle')}</h2>
        <Link to={allLink} className="small">
          {t('clinicNav.appointments')}
        </Link>
      </div>
      {body}
    </section>
  )
}

/** Whole so‘m for plotting only; money shown to people is always formatted from the API string. */
const plotValue = (amount: string) => Number(toTiyin(amount) / 100n)

function FinanceTrend({ today }: { today: string }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [branch] = useBranchParam()
  const from = addDays(today, -(CHART_DAYS - 1))
  const report = useQuery(`dash-daily:${clinicId}:${branch ?? ''}:${from}:${today}`, () => api.finance.daily(clinicId, { branchId: branch, from, to: today }))

  return (
    <section className="card stack" aria-labelledby="trend-title">
      <div className="section-head">
        <h2 id="trend-title">{t('dash.chartTitle')}</h2>
        <Link to={`/clinic/${clinicId}/reports${branch ? `?branch=${encodeURIComponent(branch)}` : ''}`} className="small">
          {t('clinicNav.reports')}
        </Link>
      </div>
      {report.data ? (
        <TrendBody rows={report.data} />
      ) : report.error !== undefined ? (
        <ApiErrorState error={report.error} onRetry={report.reload} />
      ) : (
        <div role="status" aria-live="polite">
          <span className="visually-hidden">{t('common.loading')}</span>
          <Skeleton variant="block" height={240} />
        </div>
      )}
    </section>
  )
}

function TrendBody({ rows }: { rows: DailyReportRow[] }) {
  const { t, locale } = useI18n()
  if (rows.every((r) => toTiyin(r.charged.amount) === 0n && toTiyin(r.collected.amount) === 0n)) {
    return <EmptyState title={t('dash.chartEmpty')} icon={Wallet} />
  }
  const days = rows.map((r) => formatLocalDay(r.date, locale))
  return (
    <>
      <LineChart
        title={t('dash.chartTitle')}
        hint={t('dash.chartHint')}
        labels={days.map((d) => d.day)}
        tipLabels={days.map((d) => `${d.weekday}, ${d.day}`)}
        formatTick={(v) => formatCompactNumber(v, locale)}
        series={[
          {
            id: 'charged',
            name: t('dash.chartCharged'),
            slot: 1,
            values: rows.map((r) => plotValue(r.charged.amount)),
            formatted: rows.map((r) => formatMoney(r.charged, locale)),
            info: <Term label={t('dash.chartCharged')}>{t('dash.chartChargedInfo')}</Term>,
          },
          {
            id: 'collected',
            name: t('dash.chartCollected'),
            slot: 2,
            values: rows.map((r) => plotValue(r.collected.amount)),
            formatted: rows.map((r) => formatMoney(r.collected, locale)),
            info: <Term label={t('dash.chartCollected')}>{t('dash.chartCollectedInfo')}</Term>,
          },
        ]}
      />
      <details className="chart-table">
        <summary>{t('dash.chartTable')}</summary>
        <div className="table-wrap">
          <table className="table table--stack">
            <thead>
              <tr>
                <th scope="col">{t('common.date')}</th>
                <th scope="col" className="num">
                  {t('dash.chartCharged')}
                </th>
                <th scope="col" className="num">
                  {t('dash.chartCollected')}
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={r.date}>
                  <th scope="row" data-label={t('common.date')}>
                    {days[i]?.weekday}, {days[i]?.day}
                  </th>
                  <td className="num" data-label={t('dash.chartCharged')}>
                    {formatMoney(r.charged, locale)}
                  </td>
                  <td className="num" data-label={t('dash.chartCollected')}>
                    {formatMoney(r.collected, locale)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </>
  )
}
