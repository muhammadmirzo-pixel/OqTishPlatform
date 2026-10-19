import { Building2, Eye, EyeOff, Gavel, MessageSquareWarning, Star } from 'lucide-react'
import { useState } from 'react'
import { useApi } from '../../api/context'
import type { AbuseReportDto, AdminReviewDto, ReportStatus, ReviewModerationStatus } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Pagination } from '../../ui/Pagination'
import { ConfirmDialog, ReasonDialog } from '../../ui/ReasonDialog'
import { EmptyState, QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader, Stars } from '../../ui/widgets'

const reportStatuses: ReportStatus[] = ['Open', 'Resolved']
const reviewStatuses: ReviewModerationStatus[] = ['Pending', 'Published', 'Hidden']

export function AdminReportsPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.reports'))
  const api = useApi()
  const [status, setStatus] = useState<ReportStatus | ''>('Open')
  const [page, setPage] = useState(1)
  const [resolving, setResolving] = useState<AbuseReportDto | null>(null)
  const toast = useToast()
  const list = useQuery(`admin-reports:${status}:${page}`, () => api.admin.reports({ status: status || undefined, page }))

  return (
    <div className="stack">
      <PageHeader icon={MessageSquareWarning} title={t('adminNav.reports')} />
      <label className="field field--inline">
        <span>{t('common.status')}</span>
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as ReportStatus | '')
            setPage(1)
          }}
        >
          <option value="">{t('common.all')}</option>
          {reportStatuses.map((s) => (
            <option key={s} value={s}>
              {t(`adm.report.${s}`)}
            </option>
          ))}
        </select>
      </label>
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState icon={MessageSquareWarning} />
          ) : (
            <>
              <ul className="list-plain stack">
                {data.items.map((r) => (
                  <li key={r.id} className="card stack">
                    <div className="row row--between">
                      <span className="row">
                        {r.targetType === 'Clinic' ? <Building2 className="icon muted" aria-hidden="true" /> : <Star className="icon muted" aria-hidden="true" />}
                        <Badge tone={r.targetType === 'Clinic' ? 'info' : 'neutral'}>{t(`adm.target.${r.targetType}`)}</Badge> <strong>{r.targetLabel ?? r.targetId}</strong>
                      </span>
                      <Badge tone={r.status === 'Open' ? 'warning' : 'success'}>{t(`adm.report.${r.status}`)}</Badge>
                    </div>
                    <p>{r.reason}</p>
                    <div className="row row--between">
                      <span className="muted small">{formatInstantDate(r.createdAt, DEFAULT_TIME_ZONE, locale)}</span>
                      {r.status === 'Open' ? (
                        <button type="button" className="btn btn--small" onClick={() => setResolving(r)}>
                          {t('adm.resolve')}
                        </button>
                      ) : (
                        r.resolution && <span className="small">{r.resolution}</span>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPage={setPage} />
            </>
          )
        }
      </QueryView>
      {resolving && (
        <ReasonDialog
          title={t('adm.resolve')}
          label={t('adm.resolution')}
          submitLabel={t('adm.resolve')}
          onSubmit={(text) => api.admin.resolveReport(resolving.id, text)}
          onDone={() => {
            setResolving(null)
            toast({ title: t('adm.done') })
            list.reload()
          }}
          onClose={() => setResolving(null)}
        />
      )}
    </div>
  )
}

export function AdminReviewsPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.reviews'))
  const api = useApi()
  const [status, setStatus] = useState<ReviewModerationStatus | ''>('Pending')
  const [page, setPage] = useState(1)
  const [action, setAction] = useState<{ kind: 'publish' | 'hide'; review: AdminReviewDto } | null>(null)
  const toast = useToast()
  const list = useQuery(`admin-reviews:${status}:${page}`, () => api.admin.reviews({ status: status || undefined, page }))
  const done = () => {
    setAction(null)
    toast({ title: t('adm.done') })
    list.reload()
  }

  return (
    <div className="stack">
      <PageHeader icon={Gavel} title={t('adminNav.reviews')} />
      <label className="field field--inline">
        <span>{t('common.status')}</span>
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as ReviewModerationStatus | '')
            setPage(1)
          }}
        >
          <option value="">{t('common.all')}</option>
          {reviewStatuses.map((s) => (
            <option key={s} value={s}>
              {t(`adm.review.${s}`)}
            </option>
          ))}
        </select>
      </label>
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState icon={Gavel} />
          ) : (
            <>
              <ul className="list-plain stack">
                {data.items.map((r) => (
                  <li key={r.id} className="card stack">
                    <div className="row row--between">
                      <span>
                        <strong>{r.clinicName}</strong> · {r.authorName}
                      </span>
                      <Stars rating={r.rating} label={t('home.ratingLabel', { rating: r.rating })} />
                    </div>
                    <p>{r.text || <span className="muted">—</span>}</p>
                    <div className="row row--between">
                      <span className="muted small">
                        <Badge>{t(`adm.review.${r.status}`)}</Badge> {formatInstantDate(r.createdAt, DEFAULT_TIME_ZONE, locale)}
                      </span>
                      <div className="row">
                        {r.status !== 'Published' && (
                          <button type="button" className="btn btn--small" onClick={() => setAction({ kind: 'publish', review: r })}>
                            <Eye className="icon" aria-hidden="true" />
                            {t('adm.publish')}
                          </button>
                        )}
                        {r.status !== 'Hidden' && (
                          <button type="button" className="btn btn--ghost btn--small" onClick={() => setAction({ kind: 'hide', review: r })}>
                            <EyeOff className="icon" aria-hidden="true" />
                            {t('adm.hide')}
                          </button>
                        )}
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPage={setPage} />
            </>
          )
        }
      </QueryView>
      {action?.kind === 'publish' && (
        <ConfirmDialog title={t('adm.publish')} body={<p>{t('adm.publishBody')}</p>} confirmLabel={t('adm.publish')} onConfirm={() => api.admin.publishReview(action.review.id)} onDone={done} onClose={() => setAction(null)} />
      )}
      {action?.kind === 'hide' && (
        <ReasonDialog
          title={`${t('adm.hide')}: ${action.review.clinicName}`}
          label={t('common.reason')}
          danger
          intro={<p>{t('adm.hideBody')}</p>}
          submitLabel={t('adm.hide')}
          onSubmit={(reason) => api.admin.hideReview(action.review.id, reason)}
          onDone={done}
          onClose={() => setAction(null)}
        />
      )}
    </div>
  )
}
