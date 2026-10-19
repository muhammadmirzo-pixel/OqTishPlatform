import { Ban, Bell, CheckCircle2, FileClock, Hourglass, LoaderCircle, RotateCcw, Search, SkipForward, XCircle, type LucideIcon } from 'lucide-react'
import { useState } from 'react'
import { useApi } from '../../api/context'
import { notificationStatuses, type NotificationStatus } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { FormError } from '../../ui/forms'
import { SelectField } from '../../ui/Select'
import { Pagination } from '../../ui/Pagination'
import { EmptyState, QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader } from '../../ui/widgets'
import { AuditTable } from '../finance/AuditTable'

export function AdminAuditPage() {
  const { t } = useI18n()
  useTitle(t('adminNav.audit'))
  const api = useApi()
  const [clinicId, setClinicId] = useState('')
  const [action, setAction] = useState('')
  const [applied, setApplied] = useState({ clinicId: '', action: '' })
  const [page, setPage] = useState(1)
  const audit = useQuery(`admin-audit:${applied.clinicId}:${applied.action}:${page}`, () =>
    api.admin.audit({ clinicId: applied.clinicId || undefined, action: applied.action || undefined, page }),
  )
  return (
    <div className="stack">
      <PageHeader icon={FileClock} title={t('adminNav.audit')} />
      <form
        className="filters"
        onSubmit={(e) => {
          e.preventDefault()
          setPage(1)
          setApplied({ clinicId: clinicId.trim(), action: action.trim() })
        }}
      >
        <label className="field">
          <span>{t('adm.clinicId')}</span>
          <input value={clinicId} onChange={(e) => setClinicId(e.target.value)} />
        </label>
        <label className="field">
          <span>{t('audit.action')}</span>
          <input value={action} placeholder="clinic.approved" onChange={(e) => setAction(e.target.value)} />
        </label>
        <button type="submit" className="btn btn--ghost">
          <Search className="icon" aria-hidden="true" />
          {t('common.search')}
        </button>
      </form>
      <QueryView query={audit}>{(p) => <AuditTable page={p} onPage={setPage} showClinic />}</QueryView>
    </div>
  )
}

const tone: Record<NotificationStatus, string> = { Pending: 'warning', Processing: 'info', Sent: 'success', Failed: 'danger', Skipped: 'neutral', Cancelled: 'neutral' }
const icon: Record<NotificationStatus, LucideIcon> = { Pending: Hourglass, Processing: LoaderCircle, Sent: CheckCircle2, Failed: XCircle, Skipped: SkipForward, Cancelled: Ban }

export function NotificationsPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.notifications'))
  const api = useApi()
  const [status, setStatus] = useState<NotificationStatus | ''>('Failed')
  const [page, setPage] = useState(1)
  const list = useQuery(`admin-notifications:${status}:${page}`, () => api.admin.notifications({ status: status || undefined, page }))
  const retry = useAction((id: string) => api.admin.retryNotification(id))
  const toast = useToast()
  return (
    <div className="stack">
      <PageHeader icon={Bell} title={t('adminNav.notifications')} subtitle={t('adm.outboxIntro')} />
      <SelectField
        inline
        label={t('common.status')}
        value={status}
        onChange={(v) => {
          setStatus(v as NotificationStatus | '')
          setPage(1)
        }}
        options={[{ value: '', label: t('common.all') }, ...notificationStatuses.map((s) => ({ value: s, label: t(`adm.outbox.${s}`) }))]}
      />
      <FormError error={retry.error} />
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState icon={Bell} />
          ) : (
            <>
              <div className="table-wrap">
                <table className="table table--stack">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.createdAt')}</th>
                      <th scope="col">{t('adm.outboxType')}</th>
                      <th scope="col">{t('common.status')}</th>
                      <th scope="col">{t('adm.attempts')}</th>
                      <th scope="col">{t('adm.lastError')}</th>
                      <th scope="col">{t('common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((n) => {
                      const StatusIcon = icon[n.status]
                      return (
                        <tr key={n.id}>
                          <td data-label={t('common.createdAt')}>{formatFullDateTime(n.createdAt, DEFAULT_TIME_ZONE, locale)}</td>
                          <td data-label={t('adm.outboxType')}>
                            <code>{n.type}</code>
                            <div className="muted small">{[t(`adm.audience.${n.audience}`), n.channel].filter(Boolean).join(' · ')}</div>
                          </td>
                          <td data-label={t('common.status')}>
                            <Badge tone={tone[n.status]}>
                              <StatusIcon className="icon" aria-hidden="true" />
                              {t(`adm.outbox.${n.status}`)}
                            </Badge>
                          </td>
                          <td data-label={t('adm.attempts')}>{n.attempts}</td>
                          <td className="small" data-label={t('adm.lastError')}>
                            {n.lastErrorCode ? <code>{n.lastErrorCode}</code> : '—'}
                            {n.lastError && <div className="muted">{n.lastError}</div>}
                            {n.sentAt && <div className="muted">{t('adm.sentAt')}: {formatFullDateTime(n.sentAt, DEFAULT_TIME_ZONE, locale)}</div>}
                          </td>
                          <td data-label={t('common.actions')}>
                            {n.status === 'Failed' && (
                              <button
                                type="button"
                                className="btn btn--ghost btn--small"
                                disabled={retry.pending}
                                onClick={async () => {
                                  const r = await retry.run(n.id)
                                  if (r.ok) {
                                    toast({ title: t('adm.done') })
                                    list.reload()
                                  }
                                }}
                              >
                                <RotateCcw className="icon" aria-hidden="true" />
                                {t('common.retry')}
                              </button>
                            )}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPage={setPage} />
            </>
          )
        }
      </QueryView>
    </div>
  )
}
