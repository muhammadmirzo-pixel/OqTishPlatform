import { AlarmClock, Banknote, CalendarClock, Inbox, Phone, RefreshCw, Store } from 'lucide-react'
import { useRef, useState, type KeyboardEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { errorMessage } from '../../api/errors'
import { useApi } from '../../api/context'
import type { AppointmentDto } from '../../api/types'
import { useNow } from '../../hooks/useNow'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { formatPhone } from '../../lib/phone'
import { Notice } from '../../ui/forms'
import { Pagination } from '../../ui/Pagination'
import { Skeleton } from '../../ui/Skeleton'
import { ApiErrorState, EmptyState } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { PageHeader } from '../../ui/widgets'
import { PendingCountdown } from '../appointments/shared'
import { appointmentWhen } from '../appointments/format'
import { useBranchParam } from './branchParam'
import { useClinic } from './clinicContext'
import { PendingActions, type TriageOutcome } from './PendingActions'

/** Requests with less than this left are flagged "urgent" (display only; the server owns the deadline). */
const URGENT_MS = 10 * 60_000

export function RequestsPage() {
  const { t } = useI18n()
  useTitle(t('req.title'))
  const api = useApi()
  const toast = useToast()
  const { clinicId } = useClinic()
  const [branch] = useBranchParam()
  const [page, setPage] = useState(1)
  const [stale, setStale] = useState<string | null>(null)
  const list = useQuery(
    `requests:${clinicId}:${branch ?? ''}:${page}`,
    () => api.clinic.appointments(clinicId, { status: 'Pending', branchId: branch, page, pageSize: 50 }),
    { refreshMs: 30000 },
  )

  function onOutcome(outcome: TriageOutcome) {
    // Success is echoed in a toast; a stale request stays on the page as a notice (toasts are never the only error).
    if (outcome.kind === 'stale') setStale(errorMessage(t, outcome.error))
    else {
      setStale(null)
      toast({ title: t(outcome.kind === 'confirmed' ? 'req.confirmed' : 'req.rejected') })
    }
    list.reload()
  }

  return (
    <div className="stack">
      <PageHeader
        icon={Inbox}
        title={t('req.title')}
        subtitle={t('req.subtitle')}
        actions={
          <button type="button" className="btn btn--ghost" onClick={list.reload} disabled={list.loading}>
            <RefreshCw className="icon" aria-hidden="true" />
            {t('common.refresh')}
          </button>
        }
      />
      <div aria-live="polite">{stale && <Notice tone="warning">{stale}</Notice>}</div>
      {list.data ? (
        list.data.items.length === 0 ? (
          <EmptyState title={t('req.empty')} icon={Inbox} />
        ) : (
          <>
            <RequestList clinicId={clinicId} items={list.data.items} total={list.data.totalCount} onOutcome={onOutcome} />
            <Pagination page={list.data.page} pageSize={list.data.pageSize} totalCount={list.data.totalCount} onPage={setPage} />
            <p className="muted small">{t('req.eligibleNote')}</p>
          </>
        )
      ) : list.error !== undefined ? (
        <ApiErrorState error={list.error} onRetry={list.reload} />
      ) : (
        <RequestSkeleton />
      )}
    </div>
  )
}

function RequestList({ clinicId, items, total, onOutcome }: { clinicId: string; items: AppointmentDto[]; total: number; onOutcome: (o: TriageOutcome) => void }) {
  const { t } = useI18n()
  const navigate = useNavigate()
  const now = useNow(5000)
  const listRef = useRef<HTMLUListElement>(null)
  const sorted = [...items].sort((a, b) => (a.expiresAt ?? '').localeCompare(b.expiresAt ?? ''))

  // Single-key shortcuts only while focus is inside the list (WCAG 2.1.4) and never while typing or choosing.
  function onKeyDown(e: KeyboardEvent<HTMLUListElement>) {
    if (e.altKey || e.ctrlKey || e.metaKey) return
    const target = e.target as HTMLElement
    if (target.closest('input, select, textarea, [contenteditable="true"]')) return
    const cards = Array.from(listRef.current?.querySelectorAll<HTMLElement>('[data-request]') ?? [])
    const current = cards.findIndex((c) => c.contains(target))
    if (e.key === 'j' || e.key === 'k') {
      e.preventDefault()
      const next = e.key === 'j' ? Math.min(cards.length - 1, current + 1) : Math.max(0, current - 1)
      cards[next]?.focus()
    } else if (e.key === 'Enter' && target.dataset.request) {
      e.preventDefault()
      navigate(`/clinic/${clinicId}/appointments/${target.dataset.request}`)
    }
  }

  return (
    <>
      <p className="row row--between muted small">
        <span>{t('req.count', { count: total })}</span>
        <span className="hide-mobile">{t('req.keys')}</span>
      </p>
      <ul ref={listRef} className="list-plain stack" onKeyDown={onKeyDown}>
        {sorted.map((a) => {
          const left = a.expiresAt ? new Date(a.expiresAt).getTime() - now : null
          const urgent = left !== null && left > 0 && left < URGENT_MS
          return (
            <li key={a.id} className={urgent ? 'card request request--urgent' : 'card request'} data-request={a.id} tabIndex={-1} aria-labelledby={`req-${a.id}`}>
              <div className="request__info">
                <div className="request__head">
                  <h2 className="request__title" id={`req-${a.id}`}>
                    <Link to={`/clinic/${clinicId}/appointments/${a.id}`}>{a.serviceName}</Link>
                  </h2>
                  <div className="request__badges">
                    {urgent && (
                      <span className="badge badge--danger">
                        <AlarmClock className="icon" aria-hidden="true" />
                        {t('req.urgent')}
                      </span>
                    )}
                    {a.expiresAt && (
                      <span className="badge badge--warning">
                        <PendingCountdown expiresAt={a.expiresAt} variant="clinic" />
                      </span>
                    )}
                  </div>
                </div>
                <RequestFacts a={a} />
              </div>
              <PendingActions clinicId={clinicId} appointment={a} onOutcome={onOutcome} />
            </li>
          )
        })}
      </ul>
    </>
  )
}

function RequestFacts({ a }: { a: AppointmentDto }) {
  const { t, locale } = useI18n()
  return (
    <ul className="request__facts">
      <li>
        <CalendarClock className="icon" aria-hidden="true" />
        <strong>{appointmentWhen(a, locale)}</strong>
      </li>
      <li>
        <Store className="icon" aria-hidden="true" />
        {a.branchName}
      </li>
      <li>
        <Banknote className="icon" aria-hidden="true" />
        {formatMoney(a.quotedPrice, locale)}
      </li>
      <li>
        <Phone className="icon" aria-hidden="true" />
        <span>
          {a.contactName} ·{' '}
          <a href={`tel:${a.contactPhone}`} className="nowrap" aria-label={`${t('req.contact')}: ${formatPhone(a.contactPhone)}`}>
            {formatPhone(a.contactPhone)}
          </a>
        </span>
      </li>
    </ul>
  )
}

function RequestSkeleton() {
  const { t } = useI18n()
  return (
    <div role="status" aria-live="polite">
      <span className="visually-hidden">{t('common.loading')}</span>
      <ul className="list-plain stack" aria-hidden="true">
        {[0, 1, 2].map((i) => (
          <li key={i} className="card request">
            <div className="request-skeleton">
              <Skeleton variant="title" width="50%" />
              <Skeleton width="70%" />
              <Skeleton width="40%" />
              <Skeleton width="60%" />
            </div>
            <div className="request-skeleton">
              <Skeleton variant="block" height={44} />
              <Skeleton variant="block" height={44} width="60%" />
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
