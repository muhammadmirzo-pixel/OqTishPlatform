import { useState } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../../api/errors'
import { useApi } from '../../api/context'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { formatPhone } from '../../lib/phone'
import { Notice } from '../../ui/forms'
import { Pagination } from '../../ui/Pagination'
import { EmptyState, QueryView } from '../../ui/states'
import { PageHeader } from '../../ui/widgets'
import { PendingCountdown } from '../appointments/shared'
import { appointmentWhen } from '../appointments/format'
import { useBranchParam } from './branchParam'
import { useClinic } from './clinicContext'
import { PendingActions, type TriageOutcome } from './PendingActions'

export function RequestsPage() {
  const { t, locale } = useI18n()
  useTitle(t('req.title'))
  const api = useApi()
  const { clinicId } = useClinic()
  const [branch] = useBranchParam()
  const [page, setPage] = useState(1)
  const [notice, setNotice] = useState<{ tone: 'success' | 'warning'; text: string } | null>(null)
  const list = useQuery(
    `requests:${clinicId}:${branch ?? ''}:${page}`,
    () => api.clinic.appointments(clinicId, { status: 'Pending', branchId: branch, page, pageSize: 50 }),
    { refreshMs: 30000 },
  )

  function onOutcome(outcome: TriageOutcome) {
    if (outcome.kind === 'stale') setNotice({ tone: 'warning', text: errorMessage(t, outcome.error) })
    else setNotice({ tone: 'success', text: t(outcome.kind === 'confirmed' ? 'req.confirmed' : 'req.rejected') })
    list.reload()
  }

  return (
    <div className="stack">
      <PageHeader
        title={t('req.title')}
        subtitle={t('req.subtitle')}
        actions={
          <button type="button" className="btn btn--ghost" onClick={list.reload}>
            {t('common.refresh')}
          </button>
        }
      />
      <div aria-live="polite">{notice && <Notice tone={notice.tone}>{notice.text}</Notice>}</div>
      <QueryView query={list}>
        {(data) => {
          const items = [...data.items].sort((a, b) => (a.expiresAt ?? '').localeCompare(b.expiresAt ?? ''))
          if (items.length === 0) return <EmptyState title={t('req.empty')} />
          return (
            <>
              <ul className="list-plain stack">
                {items.map((a) => (
                  <li key={a.id} className="card request">
                    <div className="request__info">
                      <div className="row row--between">
                        <strong>
                          <Link to={`/clinic/${clinicId}/appointments/${a.id}`}>{a.serviceName}</Link>
                        </strong>
                        {a.expiresAt && (
                          <span className="badge badge--warning">
                            <PendingCountdown expiresAt={a.expiresAt} variant="clinic" />
                          </span>
                        )}
                      </div>
                      <div>{appointmentWhen(a, locale)}</div>
                      <div className="muted small">
                        {a.branchName} · {formatMoney(a.quotedPrice, locale)}
                      </div>
                      <div className="small">
                        {a.contactName} · <a href={`tel:${a.contactPhone}`}>{formatPhone(a.contactPhone)}</a>
                      </div>
                    </div>
                    <PendingActions clinicId={clinicId} appointment={a} onOutcome={onOutcome} />
                  </li>
                ))}
              </ul>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPage={setPage} />
              <p className="muted small">{t('req.eligibleNote')}</p>
            </>
          )
        }}
      </QueryView>
    </div>
  )
}
